using System.Text.Json;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente GRATUITA y NACIONAL basada en servidores ArcGIS REST (Esri), que usan muchas ciudades
/// de EE.UU. que NO están en Socrata (SF, Seattle, San Diego, Las Vegas, Sacramento, etc.).
/// Consulta la capa con /query?where=1=1&outFields=*&f=json y mapea features[].attributes al
/// mismo esquema con <see cref="MapeoDatosAbiertos"/>. Manejado por configuración: agregar una
/// ciudad = una entrada. Se activa con Leads:Fuente:Proveedor = "ArcGis".
/// </summary>
public class FuenteArcGis : IFuenteProyectos
{
    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteArcGis> _logger;

    public FuenteArcGis(HttpClient http, OpcionesLeads opciones, ILogger<FuenteArcGis> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true;

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var porCiudad = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 50) : 1000;
        // Permite reusar la misma lista si se quiere; si está vacía, usa los predefinidos ArcGIS.
        var datasets = _opciones.Datasets.Count > 0 ? _opciones.Datasets : PorDefecto;
        var leads = new List<ProyectoLead>();

        foreach (var ds in datasets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // ds.Url = base del FeatureServer; se consulta la capa 0.
                var baseUrl = ds.Url.TrimEnd('/');
                var url = $"{baseUrl}/0/query?where=1%3D1&outFields=*&f=json&resultRecordCount={porCiudad}"
                    + (string.IsNullOrWhiteSpace(ds.Orden) ? "" : $"&orderByFields={Uri.EscapeDataString(ds.Orden)}");

                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{Fuente}: ArcGIS respondió {Codigo}", ds.Fuente, (int)resp.StatusCode);
                    continue;
                }
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("features", out var feats) || feats.ValueKind != JsonValueKind.Array)
                    continue;

                var antes = leads.Count;
                foreach (var f in feats.EnumerateArray())
                {
                    if (!f.TryGetProperty("attributes", out var attrs)) continue;
                    var lead = MapeoDatosAbiertos.Mapear(attrs, ds, EstadosVerificacion.PermisoOficial);
                    if (lead is null) continue;
                    if (lead.Presupuesto > 0 && lead.Presupuesto < criterios.PresupuestoMinimo) continue;
                    leads.Add(lead);
                }
                _logger.LogInformation("{Fuente}: {N} lead(s) (ArcGIS).", ds.Fuente, leads.Count - antes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando ArcGIS de {Fuente}", ds.Fuente);
            }
        }

        return JsonSerializer.Serialize(leads);
    }

    /// <summary>Capas ArcGIS predefinidas (verificadas). Se usan si Leads:Datasets está vacío.</summary>
    private static readonly List<DatasetAbierto> PorDefecto = new()
    {
        new() { Fuente = "Las Vegas NV", Prefijo = "LV",
            Url = "https://services1.arcgis.com/F1v0ufATbBQScMtY/ArcGIS/rest/services/OpenData_Building_Permits_/FeatureServer",
            Orden = "ISSDTTM DESC",
            Clave = "APNO", Nombre = "WORKTYPE", Tipo = "APTYPE",
            DireccionCols = "APL_ADDRESS", Ciudad = "@Las Vegas", Estado = "@NV", Zip = "ZIP",
            Presupuesto = "DECLVLTN", Empresa = "APPLICANT", Contacto = "LEGALOWNER", Fecha = "ISSDTTM" },

        new() { Fuente = "Sacramento CA", Prefijo = "SAC",
            Url = "https://services5.arcgis.com/54falWtcpty3V47Z/ArcGIS/rest/services/BldgPermitIssued_CurrentYear/FeatureServer",
            Orden = "Status_Date DESC",
            Clave = "Application", Nombre = "Project_Name", Tipo = "Type",
            DireccionCols = "Address", Ciudad = "@Sacramento", Estado = "@CA", Zip = "ZIP",
            Presupuesto = "Valuation", Empresa = "Contractor", Fecha = "Status_Date", Nota = "Work_Desc" }
    };
}
