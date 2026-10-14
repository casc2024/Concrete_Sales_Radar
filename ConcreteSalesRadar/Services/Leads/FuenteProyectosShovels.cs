using System.Web;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente real de proyectos usando la API de permisos de construcción de Shovels.ai
/// (GET https://api.shovels.ai/v2/permits/search, auth por cabecera X-API-Key).
/// Devuelve el JSON crudo con los permisos; la normalización la hace la IA.
/// </summary>
public class FuenteProyectosShovels : IFuenteProyectos
{
    private const string BaseUrl = "https://api.shovels.ai/v2/permits/search";

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteProyectosShovels> _logger;

    public FuenteProyectosShovels(HttpClient http, IOptions<OpcionesLeads> opciones,
        ILogger<FuenteProyectosShovels> logger)
    {
        _http = http;
        _opciones = opciones.Value;
        _logger = logger;
    }

    private FuenteApiOpciones Op => _opciones.Fuente;

    public bool EstaConfigurada =>
        !string.IsNullOrWhiteSpace(Op.ApiKey) && !string.IsNullOrWhiteSpace(Op.GeoId);

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurada) return null;

        try
        {
            var url = ConstruirUrl(criterios);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-API-Key", Op.ApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var respuesta = await _http.SendAsync(request, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning("Shovels respondió {Codigo}: {Cuerpo}",
                    (int)respuesta.StatusCode, Recortar(cuerpo, 500));
                return null;
            }
            return cuerpo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar la API de Shovels");
            return null;
        }
    }

    private string ConstruirUrl(CriteriosBusqueda criterios)
    {
        var hoy = DateTime.UtcNow.Date;
        var desde = hoy.AddDays(-Math.Max(1, Op.DiasAtras));

        var q = HttpUtility.ParseQueryString(string.Empty);
        q["geo_id"] = Op.GeoId;
        q["permit_from"] = desde.ToString("yyyy-MM-dd");
        q["permit_to"] = hoy.ToString("yyyy-MM-dd");
        // Shovels espera el valor en centavos.
        q["permit_min_job_value"] = ((long)(criterios.PresupuestoMinimo * 100m)).ToString();
        // size: no pedimos más de lo que vamos a insertar (máx. 100 por la API).
        q["size"] = Math.Clamp(_opciones.MaxPorEjecucion, 1, 100).ToString();

        var tipo = MapearTipo(criterios.TipoProyecto);
        if (tipo is not null) q["property_type"] = tipo;
        if (Op.SoloConContratista) q["permit_has_contractor"] = "true";

        return $"{BaseUrl}?{q}";
    }

    /// <summary>Mapea nuestro "Tipo" al enum property_type de Shovels; "All" => sin filtro.</summary>
    private static string? MapearTipo(string tipo) => tipo.Trim().ToLowerInvariant() switch
    {
        "all" or "" => null,
        "commercial" or "comercial" => "commercial",
        "residential" or "residencial" => "residential",
        "industrial" => "industrial",
        "office" or "oficina" => "office",
        _ => null
    };

    private static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max] + "…";
}
