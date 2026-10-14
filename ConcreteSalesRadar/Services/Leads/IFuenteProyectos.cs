using System.Text;
using System.Web;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>Criterios con los que el job busca proyectos en la fuente externa.</summary>
public record CriteriosBusqueda(string Ubicacion, int RadioMillas, string TipoProyecto, decimal PresupuestoMinimo);

/// <summary>
/// Fuente de datos reales de proyectos de construcción. Devuelve la respuesta cruda del
/// proveedor (JSON como texto); la normalización a nuestra tabla la hace la IA.
/// </summary>
public interface IFuenteProyectos
{
    /// <summary>True cuando hay un endpoint configurado.</summary>
    bool EstaConfigurada { get; }

    /// <summary>Consulta la API real y devuelve el cuerpo crudo, o null si no está configurada o falla.</summary>
    Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct);
}

/// <summary>
/// Adaptador HTTP genérico: arma la consulta con los criterios como parámetros de query,
/// agrega la API key en la cabecera indicada y devuelve el cuerpo tal cual. Sirve para la
/// mayoría de APIs REST de construcción/permisos; si un proveedor necesita otra forma de
/// request, se ajusta aquí.
/// </summary>
public class FuenteProyectosHttp : IFuenteProyectos
{
    private readonly HttpClient _http;
    private readonly FuenteApiOpciones _opciones;
    private readonly ILogger<FuenteProyectosHttp> _logger;

    public FuenteProyectosHttp(HttpClient http, IOptions<OpcionesLeads> opciones, ILogger<FuenteProyectosHttp> logger)
    {
        _http = http;
        _opciones = opciones.Value.Fuente;
        _logger = logger;
    }

    public bool EstaConfigurada => !string.IsNullOrWhiteSpace(_opciones.Endpoint);

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurada) return null;

        try
        {
            using var request = ConstruirRequest(criterios);
            using var respuesta = await _http.SendAsync(request, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning("La fuente de leads respondió {Codigo}: {Cuerpo}",
                    (int)respuesta.StatusCode, Recortar(cuerpo, 500));
                return null;
            }
            return cuerpo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar la fuente de leads");
            return null;
        }
    }

    private HttpRequestMessage ConstruirRequest(CriteriosBusqueda criterios)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["location"] = criterios.Ubicacion;
        query["radius_miles"] = criterios.RadioMillas.ToString();
        query["project_type"] = criterios.TipoProyecto;
        query["min_budget"] = ((long)criterios.PresupuestoMinimo).ToString();
        foreach (var (clave, valor) in _opciones.QueryExtra)
            query[clave] = valor;

        var esPost = _opciones.Metodo.Equals("POST", StringComparison.OrdinalIgnoreCase);
        var metodo = esPost ? HttpMethod.Post : HttpMethod.Get;

        var baseUrl = _opciones.Endpoint!;
        var url = esPost ? baseUrl : $"{baseUrl}{(baseUrl.Contains('?') ? '&' : '?')}{query}";

        var request = new HttpRequestMessage(metodo, url);
        if (esPost)
            request.Content = new StringContent(query.ToString() ?? "",
                Encoding.UTF8, "application/x-www-form-urlencoded");

        if (!string.IsNullOrWhiteSpace(_opciones.ApiKey))
            request.Headers.TryAddWithoutValidation(
                _opciones.NombreCabeceraApiKey, $"{_opciones.PrefijoApiKey}{_opciones.ApiKey}");

        return request;
    }

    private static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max] + "…";
}
