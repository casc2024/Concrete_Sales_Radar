using System.Globalization;
using System.Text.Json;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente GRATUITA: proyectos de carreteras/puentes de TxDOT (licitaciones públicas estatales,
/// dataset "Project Information" de data.texas.gov, actualizado a diario). Encaja con PAVING,
/// GRADING y obra nueva vial. Trae, cuando está disponible, el project manager de TxDOT como
/// contacto real (nombre + email) — verificado en vivo, no todas las filas lo traen.
/// Se activa con Leads:Fuente:Proveedor = "TxDot". Solo cubre Texas.
/// </summary>
public class FuenteTxDot : IFuenteProyectos
{
    private const string Url = "https://data.texas.gov/resource/drau-zphx.json";

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteTxDot> _logger;

    public FuenteTxDot(HttpClient http, OpcionesLeads opciones, ILogger<FuenteTxDot> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true; // Datos públicos: no requiere configuración.

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var tope = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 200) : 500;
        var leads = new List<ProyectoLead>();

        try
        {
            var url = $"{Url}?$limit={tope}&$order=project_estimate_low_bid DESC";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("TxDOT respondió {Codigo}", (int)resp.StatusCode);
                return JsonSerializer.Serialize(leads);
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return JsonSerializer.Serialize(leads);

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var id = Txt(row, "project_id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var presupuesto = Dec(Txt(row, "project_estimate_low_bid"))
                                   is var v && v > 0 ? v : Dec(Txt(row, "sealed_engineer_s_estimate"));
                if (presupuesto > 0 && presupuesto < criterios.PresupuestoMinimo) continue;

                leads.Add(new ProyectoLead
                {
                    ClaveExterna = "TXDOT-" + id,
                    NombreProyecto = Txt(row, "project_name") ?? Txt(row, "short_description") ?? $"Proyecto {id}",
                    Tipo = Txt(row, "short_description") ?? Txt(row, "project_type"),
                    Direccion = Juntar(", ", Txt(row, "highway"), Txt(row, "county") is { } c ? $"{c} County" : null, "TX"),
                    Presupuesto = presupuesto,
                    FechaInicio = Fecha(Txt(row, "project_estimated_let_date")),
                    NombreContacto = Txt(row, "construction_manager"),
                    Email = Txt(row, "construction_manager_email"),
                    EmpresaContacto = "TxDOT" + (Txt(row, "local_agency_name") is { } la ? $" / {la}" : ""),
                    Fuente = "TxDOT (licitación pública)",
                    EstadoVerificacion = EstadosVerificacion.LicitacionMunicipal,
                    Nota = Juntar(" · ", Txt(row, "project_description"), Txt(row, "type_of_work"),
                                  Txt(row, "project_sub_type_description")),
                    Yardas = Yardas(presupuesto),
                    Prioridad = Prioridad(presupuesto)
                });
            }
            _logger.LogInformation("TxDOT: {N} proyecto(s).", leads.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consultando TxDOT");
        }

        return JsonSerializer.Serialize(leads);
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

    private static string? Juntar(string sep, params string?[] partes)
    {
        var r = string.Join(sep, partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(r) ? null : r;
    }

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
