using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente de "investigación web" (gratis): busca en Brave Search anuncios de desarrollos de
/// construcción (residenciales/multifamiliares/industriales en fase de planificación) y arma un
/// texto crudo con los resultados y el contenido de las páginas top. La IA (Gemini) extrae de ahí
/// los leads estructurados (developer, contacto, fase, estimaciones).
///
/// Cubre lo que las APIs de permisos NO dan: proyectos ANUNCIADOS que aún no son permiso.
/// </summary>
public class FuenteBusquedaWeb : IFuenteProyectos
{
    private const string BraveEndpoint = "https://api.search.brave.com/res/v1/web/search";

    private readonly HttpClient _http;
    private readonly FuenteApiOpciones _opc;
    private readonly ILogger<FuenteBusquedaWeb> _logger;
    private readonly string? _apiKey;

    public FuenteBusquedaWeb(HttpClient http, OpcionesLeads opciones, ILogger<FuenteBusquedaWeb> logger)
    {
        _http = http;
        _opc = opciones.Fuente;
        _logger = logger;
        _apiKey = !string.IsNullOrWhiteSpace(_opc.BraveApiKey)
            ? _opc.BraveApiKey
            : Environment.GetEnvironmentVariable("BRAVE_API_KEY");
    }

    public bool EstaConfigurada => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurada) return null;

        var region = string.IsNullOrWhiteSpace(criterios.Ubicacion) ? "Texas" : criterios.Ubicacion.Trim();
        var consultas = ConsultasEfectivas(region);

        var resultados = new List<ResultadoWeb>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < consultas.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // Brave gratis = 1 req/seg: pequeña pausa entre consultas (menos antes de la primera).
            if (i > 0) await Task.Delay(1100, ct);

            foreach (var r in await BuscarAsync(consultas[i], ct))
                if (vistos.Add(r.Url)) resultados.Add(r);
        }

        if (resultados.Count == 0)
        {
            _logger.LogWarning("BusquedaWeb: Brave no devolvió resultados.");
            return null;
        }

        // Abre las primeras N páginas para extraer texto completo (de ahí salen los contactos).
        var paginas = new Dictionary<string, string>();
        foreach (var r in resultados.Take(Math.Max(0, _opc.MaxPaginasAbrir)))
        {
            var texto = await DescargarTextoAsync(r.Url, ct);
            if (!string.IsNullOrWhiteSpace(texto)) paginas[r.Url] = texto;
        }

        return ArmarCrudo(region, resultados, paginas);
    }

    private List<string> ConsultasEfectivas(string region)
    {
        if (_opc.ConsultasWeb.Count > 0)
            return _opc.ConsultasWeb.Select(q => q.Replace("{region}", region)).ToList();

        return new List<string>
        {
            $"new residential development {region} lots groundbreaking",
            $"new master planned community {region} developer homes",
            $"multifamily apartment development approved {region}",
            $"new industrial park warehouse development {region} construction",
            $"data center development announced {region}",
            $"new subdivision plat approved {region} homes"
        };
    }

    private async Task<List<ResultadoWeb>> BuscarAsync(string consulta, CancellationToken ct)
    {
        try
        {
            var count = Math.Clamp(_opc.MaxResultadosPorConsulta, 1, 20);
            var url = $"{BraveEndpoint}?q={Uri.EscapeDataString(consulta)}&count={count}" +
                      "&freshness=py&country=us&search_lang=en&result_filter=web";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("X-Subscription-Token", _apiKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var cuerpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("BusquedaWeb: Brave respondió {Codigo} para '{Q}'.",
                    (int)resp.StatusCode, consulta);
                return new();
            }

            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return new();

            var lista = new List<ResultadoWeb>();
            foreach (var it in arr.EnumerateArray())
            {
                var u = Prop(it, "url");
                if (string.IsNullOrWhiteSpace(u)) continue;
                lista.Add(new ResultadoWeb(u, Prop(it, "title"), Prop(it, "description"),
                    Prop(it, "age") ?? Prop(it, "page_age"), consulta));
            }
            return lista;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BusquedaWeb: error consultando Brave con '{Q}'.", consulta);
            return new();
        }
    }

    private async Task<string?> DescargarTextoAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "text/html");
            using var resp = await _http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            var tipo = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!tipo.Contains("html") && !tipo.Contains("text")) return null;

            var html = await resp.Content.ReadAsStringAsync(cts.Token);
            return LimpiarHtml(html);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BusquedaWeb: no se pudo abrir {Url}.", url);
            return null;
        }
    }

    /// <summary>Quita scripts/estilos/etiquetas y colapsa espacios; recorta a ~4000 chars.</summary>
    private static string LimpiarHtml(string html)
    {
        html = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");
        html = WebUtility.HtmlDecode(html);
        html = Regex.Replace(html, "\\s+", " ").Trim();
        return html.Length <= 4000 ? html : html[..4000];
    }

    private static string ArmarCrudo(string region, List<ResultadoWeb> resultados, Dictionary<string, string> paginas)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RESULTADOS DE BÚSQUEDA WEB (Brave) — región objetivo: {region}");
        sb.AppendLine("Cada bloque es un resultado. PAGE_TEXT (si existe) es el contenido de la página.");
        sb.AppendLine();
        var n = 0;
        foreach (var r in resultados)
        {
            n++;
            sb.AppendLine($"### RESULTADO {n}");
            sb.AppendLine($"SOURCE_URL: {r.Url}");
            if (!string.IsNullOrWhiteSpace(r.Titulo)) sb.AppendLine($"TITLE: {r.Titulo}");
            if (!string.IsNullOrWhiteSpace(r.Edad)) sb.AppendLine($"DATE: {r.Edad}");
            if (!string.IsNullOrWhiteSpace(r.Snippet)) sb.AppendLine($"SNIPPET: {r.Snippet}");
            if (paginas.TryGetValue(r.Url, out var texto)) sb.AppendLine($"PAGE_TEXT: {texto}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string? Prop(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private record ResultadoWeb(string Url, string? Titulo, string? Snippet, string? Edad, string Consulta);
}
