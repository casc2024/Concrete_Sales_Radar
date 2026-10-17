using System.Globalization;
using System.Text.Json;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente GRATUITA y NACIONAL: contratos de construcción federales YA ADJUDICADOS (no solo
/// licitaciones como SAM.gov), vía la API oficial de USASpending.gov. Un contrato adjudicado es
/// una señal más fuerte que una licitación: el trabajo ya se ganó y va a ejecutarse. Sin API key.
/// Se activa con Leads:Fuente:Proveedor = "UsaSpendingAwards". Reusa los NAICS de construcción
/// configurados en Leads:Fuente:SamNaics (mismo concepto que SAM.gov).
/// </summary>
public class FuenteUsaSpending : IFuenteProyectos
{
    private const string UrlBusqueda = "https://api.usaspending.gov/api/v2/search/spending_by_award/";

    private static readonly string[] NaicsPorDefecto =
        { "236220", "236116", "237310", "237110", "238110" };

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteUsaSpending> _logger;

    public FuenteUsaSpending(HttpClient http, OpcionesLeads opciones, ILogger<FuenteUsaSpending> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true; // API pública, sin key.

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var tope = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 100) : 500;
        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, _opciones.Fuente.DiasAtras)).ToString("yyyy-MM-dd");
        var hasta = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var naics = string.IsNullOrWhiteSpace(_opciones.Fuente.SamNaics)
            ? NaicsPorDefecto
            : _opciones.Fuente.SamNaics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var leads = new List<ProyectoLead>();
        foreach (var code in naics)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    filters = new
                    {
                        naics_codes = new { require = new[] { code } },
                        time_period = new[] { new { start_date = desde, end_date = hasta } },
                        award_type_codes = new[] { "A", "B", "C", "D" }
                    },
                    fields = new[]
                    {
                        "Award ID", "Recipient Name", "Award Amount", "Description",
                        "Place of Performance City Code", "Place of Performance State Code",
                        "Start Date", "generated_internal_id", "Awarding Agency"
                    },
                    page = 1,
                    limit = Math.Clamp(tope / Math.Max(1, naics.Length), 1, 100),
                    sort = "Award Amount",
                    order = "desc"
                });

                using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(UrlBusqueda, content, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("USASpending NAICS {Code} respondió {Codigo}.", code, (int)resp.StatusCode);
                    continue;
                }
                var cuerpo = await resp.Content.ReadAsStringAsync(ct);
                MapearRespuesta(cuerpo, leads, criterios);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USASpending: error consultando NAICS {Code}", code);
            }
        }

        _logger.LogInformation("USASpending: {N} contrato(s) adjudicado(s).", leads.Count);
        return JsonSerializer.Serialize(leads);
    }

    private static void MapearRespuesta(string cuerpo, List<ProyectoLead> leads, CriteriosBusqueda criterios)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var r in arr.EnumerateArray())
        {
            var genId = Txt(r, "generated_internal_id");
            if (string.IsNullOrWhiteSpace(genId)) continue;

            var monto = Dec(Txt(r, "Award Amount"));
            if (monto > 0 && monto < criterios.PresupuestoMinimo) continue;

            var ciudad = Txt(r, "Place of Performance City Code");
            var estado = Txt(r, "Place of Performance State Code");

            leads.Add(new ProyectoLead
            {
                ClaveExterna = "USA-" + genId,
                NombreProyecto = Txt(r, "Description") ?? Txt(r, "Award ID") ?? "Contrato federal",
                Tipo = "Federal (adjudicado)",
                Direccion = string.Join(", ", new[] { ciudad, estado }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Presupuesto = monto,
                FechaInicio = Fecha(Txt(r, "Start Date")),
                EmpresaContacto = Txt(r, "Recipient Name"),
                Fuente = "USASpending.gov (federal adjudicado)",
                EstadoVerificacion = EstadosVerificacion.LicitacionMunicipal,
                Nota = Txt(r, "Awarding Agency"),
                Prioridad = 65
            });
        }
    }

    private static string? Txt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) { var s = v.GetString(); return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
        return v.ValueKind == JsonValueKind.Number ? v.ToString() : null;
    }

    private static decimal Dec(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 0m;

    private static DateTime? Fecha(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
}
