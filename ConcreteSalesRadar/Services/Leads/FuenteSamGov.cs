using System.Globalization;
using System.Text.Json;
using System.Web;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente federal GRATUITA: oportunidades/licitaciones de construcción de todo EE.UU. vía la
/// API de SAM.gov (Opportunities v2). Cobertura nacional, pero solo lo federal. Requiere una API
/// key gratuita (sam.gov / api.data.gov). Se activa con Leads:Fuente:Proveedor = "SamGov".
///
/// Mapea cada oportunidad a un lead con agencia, lugar de ejecución y el punto de contacto
/// (nombre, email, teléfono) que publica el propio aviso. No inventa datos.
/// </summary>
public class FuenteSamGov : IFuenteProyectos
{
    private const string BaseUrl = "https://api.sam.gov/opportunities/v2/search";

    // NAICS de construcción por defecto: edificación comercial/residencial, carreteras/puentes,
    // obra pesada (agua/alcantarillado) y cimentación/concreto estructural.
    private static readonly string[] NaicsPorDefecto =
        { "236220", "236116", "237310", "237110", "238110" };

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteSamGov> _logger;
    private readonly string? _apiKey;

    public FuenteSamGov(HttpClient http, OpcionesLeads opciones, ILogger<FuenteSamGov> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
        _apiKey = !string.IsNullOrWhiteSpace(opciones.Fuente.SamApiKey)
            ? opciones.Fuente.SamApiKey
            : Environment.GetEnvironmentVariable("SAM_GOV_API_KEY");
    }

    public bool EstaConfigurada => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurada)
        {
            _logger.LogInformation("SAM.gov sin API key (Leads:Fuente:SamApiKey o SAM_GOV_API_KEY).");
            return null;
        }

        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, _opciones.Fuente.DiasAtras)).ToString("MM/dd/yyyy");
        var hasta = DateTime.UtcNow.ToString("MM/dd/yyyy");
        var tope = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 100) : 1000;
        var naics = string.IsNullOrWhiteSpace(_opciones.Fuente.SamNaics)
            ? NaicsPorDefecto
            : _opciones.Fuente.SamNaics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var leads = new List<ProyectoLead>();
        foreach (var code in naics)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var q = HttpUtility.ParseQueryString(string.Empty);
                q["api_key"] = _apiKey;
                q["postedFrom"] = desde;
                q["postedTo"] = hasta;
                q["limit"] = tope.ToString();
                q["ncode"] = code;
                q["ptype"] = "o,p,k"; // Solicitation, Presolicitation, Combined synopsis

                using var resp = await _http.GetAsync($"{BaseUrl}?{q}", ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("SAM.gov NAICS {Code} respondió {Codigo}.", code, (int)resp.StatusCode);
                    continue;
                }
                var cuerpo = await resp.Content.ReadAsStringAsync(ct);
                MapearRespuesta(cuerpo, leads);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SAM.gov: error consultando NAICS {Code}", code);
            }
        }

        _logger.LogInformation("SAM.gov: {N} oportunidad(es) federal(es).", leads.Count);
        return JsonSerializer.Serialize(leads);
    }

    private static void MapearRespuesta(string cuerpo, List<ProyectoLead> leads)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        if (!doc.RootElement.TryGetProperty("opportunitiesData", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var o in arr.EnumerateArray())
        {
            var id = Txt(o, "noticeId");
            var titulo = Txt(o, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(titulo)) continue;

            var (nombre, email, telefono) = PrimerContacto(o);
            leads.Add(new ProyectoLead
            {
                ClaveExterna = "SAM-" + id,
                NombreProyecto = titulo!,
                Tipo = "Federal: " + (Txt(o, "type") ?? "Opportunity"),
                Direccion = Lugar(o),
                Presupuesto = MontoPremio(o),
                FechaInicio = Fecha(Txt(o, "postedDate")),
                EmpresaContacto = Txt(o, "fullParentPathName") ?? Txt(o, "organizationType"),
                NombreContacto = nombre,
                Email = email,
                Telefono = telefono,
                Fuente = "SAM.gov (federal)",
                EstadoVerificacion = EstadosVerificacion.LicitacionMunicipal, // licitación/RFP pública
                Nota = Recortar(Txt(o, "uiLink") ?? Txt(o, "solicitationNumber"), 480),
                Prioridad = 60
            });
        }
    }

    private static (string? nombre, string? email, string? tel) PrimerContacto(JsonElement o)
    {
        if (o.TryGetProperty("pointOfContact", out var pocs) && pocs.ValueKind == JsonValueKind.Array)
            foreach (var p in pocs.EnumerateArray())
                return (Txt(p, "fullName"), Txt(p, "email"), Txt(p, "phone"));
        return (null, null, null);
    }

    private static string? Lugar(JsonElement o)
    {
        if (!o.TryGetProperty("placeOfPerformance", out var p) || p.ValueKind != JsonValueKind.Object)
            return null;
        string? city = p.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.Object ? Txt(c, "name") : null;
        string? st = p.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object ? Txt(s, "name") ?? Txt(s, "code") : null;
        string? zip = Txt(p, "zip");
        var partes = new[] { city, st, zip }.Where(x => !string.IsNullOrWhiteSpace(x));
        var r = string.Join(", ", partes);
        return string.IsNullOrWhiteSpace(r) ? null : r;
    }

    private static decimal MontoPremio(JsonElement o)
    {
        if (o.TryGetProperty("award", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            var s = Txt(a, "amount");
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        }
        return 0m;
    }

    private static string? Txt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) { var s = v.GetString(); return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
        return v.ValueKind == JsonValueKind.Number ? v.ToString() : null;
    }

    private static DateTime? Fecha(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    private static string? Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
