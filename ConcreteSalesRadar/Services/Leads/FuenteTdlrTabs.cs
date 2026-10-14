using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente GRATUITA basada en TDLR TABS (registro estatal de proyectos de construcción comercial
/// de Texas por accesibilidad). Todo proyecto comercial debe registrarse, así que es fresco,
/// estatal y trae dueño + teléfono + dirección + costo — lo mismo que cobra Shovels.
///
/// No hay API oficial: se replica el buscador DataTables (POST /TABS/Search/SearchProjects) para
/// listar proyectos por condado/fecha, y se lee la página de detalle de cada uno para el contacto.
/// Son datos públicos (Open Records de Texas). Se mantiene volumen modesto (MaxPorEjecucion).
/// Se activa con Leads:Fuente:Proveedor = "TdlrTabs".
/// </summary>
public class FuenteTdlrTabs : IFuenteProyectos
{
    private const string UrlBuscar = "https://www.tdlr.texas.gov/TABS/Search/SearchProjects";
    private const string UrlDetalle = "https://www.tdlr.texas.gov/TABS/Search/Print/";

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteTdlrTabs> _logger;

    public FuenteTdlrTabs(HttpClient http, OpcionesLeads opciones, ILogger<FuenteTdlrTabs> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true; // Datos públicos: no requiere API key.

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, _opciones.Fuente.DiasAtras)).ToString("MM/dd/yyyy");
        var tope = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 100) : 500;

        // 1) Listar candidatos. Si hay condados configurados, uno por uno; si no, TODO Texas.
        var candidatos = new List<(string num, string nombre, decimal costo)>();
        foreach (var codigo in CondigosAConsultar())
        {
            var etiqueta = string.IsNullOrEmpty(codigo) ? "Texas (estatal)" : $"condado {codigo}";
            try
            {
                var lista = await BuscarCondadoAsync(codigo, desde, tope, ct);
                foreach (var c in lista)
                    if (c.costo >= criterios.PresupuestoMinimo)
                        candidatos.Add(c);
                _logger.LogInformation("TABS {Etiqueta}: {N} proyecto(s) sobre el mínimo.", etiqueta, lista.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TABS: error listando {Etiqueta}", etiqueta);
            }
        }

        // 2) Mejores por costo y deduplicados, hasta el tope.
        var mejores = candidatos
            .GroupBy(c => c.num).Select(g => g.First())
            .OrderByDescending(c => c.costo)
            .Take(tope)
            .ToList();

        // 3) Detalle de cada uno (dueño, teléfono, dirección, scope).
        var leads = new List<ProyectoLead>();
        foreach (var c in mejores)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lead = await LeerDetalleAsync(c.num, c.nombre, c.costo, ct);
                if (lead is not null) leads.Add(lead);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TABS: no se pudo leer el detalle de {Num}", c.num);
            }
        }

        _logger.LogInformation("TABS: {N} lead(s) listos de TDLR.", leads.Count);
        return JsonSerializer.Serialize(leads);
    }

    /// <summary>Lista de códigos de condado a consultar; un solo "" = búsqueda estatal.</summary>
    private IEnumerable<string> CondigosAConsultar()
    {
        var cfg = _opciones.Fuente.Condados;
        if (string.IsNullOrWhiteSpace(cfg)) return new[] { "" }; // Todo Texas.
        return cfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<List<(string num, string nombre, decimal costo)>> BuscarCondadoAsync(
        string codigoCondado, string desde, int tope, CancellationToken ct)
    {
        var campos = new Dictionary<string, string>
        {
            ["draw"] = "1",
            ["start"] = "0",
            ["length"] = tope.ToString(),
            ["ProjectStatus"] = "3008", // Project Registered (activos/nuevos)
            ["RegistrationDateBegin"] = desde
        };
        if (!string.IsNullOrEmpty(codigoCondado)) campos["LocationCounty"] = codigoCondado;
        var form = new FormUrlEncodedContent(campos);

        using var resp = await _http.PostAsync(UrlBuscar, form, ct);
        resp.EnsureSuccessStatusCode();
        var cuerpo = await resp.Content.ReadAsStringAsync(ct);

        var resultado = new List<(string, string, decimal)>();
        using var doc = JsonDocument.Parse(cuerpo);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return resultado;

        foreach (var row in data.EnumerateArray())
        {
            var num = row.TryGetProperty("ProjectNumber", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(num)) continue;
            var nombre = row.TryGetProperty("ProjectName", out var pn) ? pn.GetString() ?? "" : "";
            var costo = row.TryGetProperty("EstimatedCost", out var ec) && ec.ValueKind == JsonValueKind.Number
                ? ec.GetDecimal() : 0m;
            resultado.Add((num!, nombre, costo));
        }
        return resultado;
    }

    private async Task<ProyectoLead?> LeerDetalleAsync(string num, string nombre, decimal costo, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(UrlDetalle + num, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var html = await resp.Content.ReadAsStringAsync(ct);
        var txt = ATextoPlano(html);

        var ownerBlock = Entre(txt, "OWNER ", "TENANT") ?? Entre(txt, "OWNER ", "DESIGN FIRM") ?? txt;

        var presupuesto = Dinero(Campo(txt, @"Estimated Cost:\s*\$([\d,]+(?:\.\d+)?)"));
        if (presupuesto <= 0) presupuesto = costo;

        return new ProyectoLead
        {
            ClaveExterna = num,
            NombreProyecto = Primero(
                Campo(txt, @"Project Name:\s*(.+?)\s*Project Number:"),
                Campo(txt, @"Facility Name:\s*(.+?)\s*Location Address:"),
                nombre, num),
            Tipo = Campo(txt, @"Type of Work:\s*(.+?)\s*Type of Funds:"),
            Direccion = Campo(txt, @"Location Address:\s*(.+?)\s*Location County:"),
            Presupuesto = presupuesto,
            FechaInicio = Fecha(Campo(txt, @"Start Date:\s*([\d/]+)")),
            EmpresaContacto = Campo(ownerBlock, @"Owner Name:\s*(.+?)\s*Owner Address:"),
            NombreContacto = Campo(ownerBlock, @"Contact Name:\s*(.+?)\s*$"),
            TelefonoEmpresa = Campo(ownerBlock, @"Owner Phone:\s*(\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4})"),
            Fuente = "TDLR TABS",
            EstadoVerificacion = EstadosVerificacion.TdlrEstatal,
            Nota = Campo(txt, @"Scope of Work:\s*(.+?)\s*Square Footage:"),
            Yardas = Yardas(presupuesto),
            Prioridad = Prioridad(presupuesto)
        };
    }

    // ---- Helpers ----

    private static string ATextoPlano(string html) =>
        Regex.Replace(
            Regex.Replace(html, "(?s)<(script|style).*?</\\1>", " ")
                 .Replace("&amp;", "&").Replace("&nbsp;", " "),
            "<[^>]+>", " ")
        is var limpio ? Regex.Replace(limpio, @"\s+", " ").Trim() : html;

    private static string? Campo(string texto, string patron)
    {
        var m = Regex.Match(texto, patron, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? Entre(string texto, string inicio, string fin)
    {
        var i = texto.IndexOf(inicio, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i += inicio.Length;
        var j = texto.IndexOf(fin, i, StringComparison.OrdinalIgnoreCase);
        return j < 0 ? texto[i..] : texto[i..j];
    }

    private static string Primero(params string?[] opciones) =>
        opciones.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o)) ?? "Proyecto TABS";

    private static decimal Dinero(string? s) =>
        decimal.TryParse((s ?? "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static DateTime? Fecha(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    private static int Yardas(decimal p) =>
        p <= 0 ? 0 : (int)Math.Clamp(Math.Round(p * 0.07m / 180m), 0, int.MaxValue);

    private static int Prioridad(decimal p) => p switch
    {
        >= 20_000_000m => 95,
        >= 10_000_000m => 85,
        >= 5_000_000m => 75,
        >= 1_000_000m => 65,
        > 0 => 55,
        _ => 45
    };
}
