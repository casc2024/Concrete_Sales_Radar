using System.Text.Json;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente GRATUITA y NACIONAL basada en portales Socrata/SODA (datos abiertos municipales).
/// Genérica y manejada por configuración: cubrir una ciudad nueva = agregar un
/// <see cref="DatasetAbierto"/> en Leads:Datasets, sin tocar código. Sin API key ni tope de costo.
/// Se activa con Leads:Fuente:Proveedor = "DatosAbiertos". El mapeo lo hace <see cref="MapeoDatosAbiertos"/>.
/// </summary>
public class FuenteDatosAbiertos : IFuenteProyectos
{
    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteDatosAbiertos> _logger;

    public FuenteDatosAbiertos(HttpClient http, OpcionesLeads opciones,
        ILogger<FuenteDatosAbiertos> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true; // Datos públicos: no requiere configuración.

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var porCiudad = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 50) : 1000;
        var datasets = _opciones.Datasets.Count > 0 ? _opciones.Datasets : PorDefecto;
        var leads = new List<ProyectoLead>();

        foreach (var ds in datasets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = ds.Url + (ds.Url.Contains('?') ? "&" : "?") + $"$limit={porCiudad}"
                    + (string.IsNullOrWhiteSpace(ds.Orden) ? "" : $"&$order={Uri.EscapeDataString(ds.Orden)}");
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{Fuente}: datos abiertos respondió {Codigo}", ds.Fuente, (int)resp.StatusCode);
                    continue;
                }
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                var antes = leads.Count;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    var lead = MapeoDatosAbiertos.Mapear(row, ds, EstadosVerificacion.PermisoOficial);
                    if (lead is null) continue;
                    if (lead.Presupuesto > 0 && lead.Presupuesto < criterios.PresupuestoMinimo) continue;
                    leads.Add(lead);
                }
                _logger.LogInformation("{Fuente}: {N} lead(s).", ds.Fuente, leads.Count - antes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando datos abiertos de {Fuente}", ds.Fuente);
            }
        }

        return JsonSerializer.Serialize(leads);
    }

    /// <summary>Datasets Socrata predefinidos (verificados). Se usan si Leads:Datasets está vacío.</summary>
    private static readonly List<DatasetAbierto> PorDefecto = new()
    {
        // Sin $order: ordenar este dataset gigante es lento; se toma la página por defecto.
        new() { Fuente = "NYC DOB", Prefijo = "NYC",
            Url = "https://data.cityofnewyork.us/resource/ipu4-2q9a.json",
            Clave = "job__", Nombre = "job_type", Tipo = "permit_type",
            DireccionCols = "house__,street_name", Ciudad = "city", Estado = "state", Zip = "zip_code",
            Empresa = "permittee_s_business_name", Telefono = "permittee_s_phone__",
            Contacto = "owner_s_business_name", Fecha = "issuance_date", Nota = "work_type" },

        new() { Fuente = "Chicago Buildings", Prefijo = "CHI",
            Url = "https://data.cityofchicago.org/resource/ydr8-5enu.json", Orden = "issue_date DESC",
            Clave = "id", Nombre = "work_description", Tipo = "permit_type",
            DireccionCols = "street_number,street_direction,street_name", Ciudad = "@Chicago", Estado = "@IL",
            Presupuesto = "reported_cost", Contacto = "contact_1_name", Fecha = "issue_date" },

        new() { Fuente = "LA Building & Safety", Prefijo = "LA",
            Url = "https://data.lacity.org/resource/pi9x-tg5x.json", Orden = "issue_date DESC",
            Clave = "permit_nbr", Nombre = "work_desc", Tipo = "permit_type",
            DireccionCols = "primary_address", Ciudad = "@Los Angeles", Estado = "@CA", Zip = "zip_code",
            Presupuesto = "valuation", Fecha = "issue_date", Nota = "use_desc" },

        new() { Fuente = "Dallas Open Data", Prefijo = "DAL",
            Url = "https://www.dallasopendata.com/resource/e7gq-4sah.json", Orden = "permit_number DESC",
            Clave = "permit_number", Nombre = "work_description", Tipo = "permit_type",
            DireccionCols = "street_address", Ciudad = "@Dallas", Estado = "@TX", Zip = "zip_code",
            Presupuesto = "value", Empresa = "contractor", Fecha = "issued_date", Nota = "land_use" },

        new() { Fuente = "Austin Open Data", Prefijo = "AUS",
            Url = "https://data.austintexas.gov/resource/3syk-w9eu.json", Orden = "issue_date DESC",
            Clave = "permit_number", Nombre = "description", Tipo = "permit_type_desc",
            DireccionCols = "original_address1", Ciudad = "original_city", Estado = "original_state", Zip = "original_zip",
            Fecha = "issue_date", Nota = "status_current" },

        new() { Fuente = "Montgomery County MD", Prefijo = "MD",
            Url = "https://data.montgomerycountymd.gov/resource/i26v-w6bd.json", Orden = "issueddate DESC",
            Clave = "permitno", Nombre = "description", Tipo = "worktype",
            DireccionCols = "stno,stname,suffix", Ciudad = "city", Estado = "state", Zip = "zip",
            Presupuesto = "declaredvaluation", Fecha = "issueddate" },

        new() { Fuente = "Cincinnati OH", Prefijo = "CIN",
            Url = "https://data.cincinnati-oh.gov/resource/uhjb-xac9.json", Orden = "issueddate DESC",
            Clave = "permitnum", Nombre = "description", Tipo = "permittype",
            DireccionCols = "originaladdress1", Ciudad = "originalcity", Estado = "originalstate",
            Presupuesto = "estprojectcostdec", Empresa = "companyname", Fecha = "issueddate" },

        new() { Fuente = "Baton Rouge LA", Prefijo = "BR",
            Url = "https://data.brla.gov/resource/7fq7-8j7r.json", Orden = "issueddate DESC",
            Clave = "permitid", Nombre = "projectdescription", Tipo = "permittype",
            DireccionCols = "streetaddress", Ciudad = "city1", Estado = "state1", Zip = "zip",
            Presupuesto = "projectvalue", Empresa = "contractorname", Contacto = "applicantname", Fecha = "issueddate" }
    };
}
