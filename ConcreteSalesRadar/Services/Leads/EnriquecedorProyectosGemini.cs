using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Normaliza leads con Google Gemini (nivel gratuito). Pensado para la fuente de investigación
/// web (FuenteBusquedaWeb): recibe resultados de búsqueda + texto de páginas y extrae proyectos
/// de construcción anunciados (developer, contacto, fase, estimaciones). No inventa contactos.
/// Los leads se marcan como baja confianza (son anuncios no verificados).
/// </summary>
public class EnriquecedorProyectosGemini : IEnriquecedorProyectosIa
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly IaOpciones _opciones;
    private readonly ILogger<EnriquecedorProyectosGemini> _logger;
    private readonly string? _apiKey;
    private readonly string _modelo;

    public EnriquecedorProyectosGemini(IOptions<OpcionesLeads> opciones, ILogger<EnriquecedorProyectosGemini> logger)
    {
        _opciones = opciones.Value.Ia;
        _logger = logger;
        _apiKey = !string.IsNullOrWhiteSpace(_opciones.ApiKey)
            ? _opciones.ApiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY")
              ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        _modelo = _opciones.Modelo.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)
            ? _opciones.Modelo
            : "gemini-2.0-flash";
    }

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<ProyectoLead>> NormalizarAsync(
        string datosCrudos, CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurado) return Array.Empty<ProyectoLead>();

        var mensajeUsuario =
            $"Región objetivo: {criterios.Ubicacion}\n" +
            $"Presupuesto mínimo de interés: {criterios.PresupuestoMinimo:C0}\n\n" +
            $"Resultados de búsqueda web y contenido de páginas (extrae los proyectos):\n{datosCrudos}";

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = mensajeUsuario } } } },
            generationConfig = new { temperature = 0.2, maxOutputTokens = 8192, responseMimeType = "application/json" }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelo}:generateContent?key={_apiKey}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req, ct);
            var cuerpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini respondió {(int)resp.StatusCode}: {Recortar(cuerpo, 300)}");

            var texto = ExtraerTextoGemini(cuerpo);
            var json = ExtraerArregloJson(texto);
            if (json is null)
            {
                _logger.LogWarning("Gemini no devolvió un arreglo JSON reconocible.");
                throw new InvalidOperationException("Gemini respondió sin JSON reconocible: " + Recortar(texto, 300));
            }

            var leads = JsonSerializer.Deserialize<List<ProyectoLead>>(json, JsonOpts) ?? new();
            foreach (var l in leads)
            {
                // Investigación web = anuncios no verificados documentalmente: baja confianza.
                l.BajaConfianza = true;
                if (string.IsNullOrWhiteSpace(l.EstadoVerificacion))
                    l.EstadoVerificacion = Models.Entities.EstadosVerificacion.Estimado;
            }
            return leads.Where(l => !string.IsNullOrWhiteSpace(l.NombreProyecto)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al normalizar leads con Gemini");
            throw new InvalidOperationException("Fallo al llamar a Gemini para normalizar leads: " + ex.Message, ex);
        }
    }

    /// <summary>Saca el texto de candidates[0].content.parts[*].text.</summary>
    private static string ExtraerTextoGemini(string cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        if (!doc.RootElement.TryGetProperty("candidates", out var cand) || cand.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var c in cand.EnumerateArray())
            if (c.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                foreach (var p in parts.EnumerateArray())
                    if (p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
        return sb.ToString();
    }

    private static string? ExtraerArregloJson(string texto)
    {
        var inicio = texto.IndexOf('[');
        var fin = texto.LastIndexOf(']');
        return (inicio >= 0 && fin > inicio) ? texto[inicio..(fin + 1)] : null;
    }

    private static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max] + "…";

    private const string SystemPrompt = """
        Eres un analista que extrae OPORTUNIDADES de proyectos de construcción a partir de
        resultados de búsqueda web (noticias, sitios de desarrolladores, comisiones de
        planificación), para una empresa que vende concreto premezclado (ready-mix).

        Recibes varios bloques de resultados (SOURCE_URL, TITLE, DATE, SNIPPET y, a veces,
        PAGE_TEXT con el contenido de la página). Devuelve SOLO un arreglo JSON (sin texto extra,
        sin ```), donde cada elemento es un PROYECTO DE CONSTRUCCIÓN ANUNCIADO, con estas claves:

        - claveExterna: string   (usa el SOURCE_URL del resultado; sirve para no duplicar)
        - direccion: string | null   (ciudad/condado/estado o dirección, lo que haya)
        - nombreProyecto: string     (obligatorio; nombre de la obra/comunidad/proyecto)
        - tipo: string | null        (Residential, Multifamily, Industrial, Warehouse, Data Center, Commercial, Road)
        - fechaInicio: string | null (ISO 8601 si hay fecha de inicio/groundbreaking; si no, null)
        - presupuesto: number        (USD invertidos/valor del proyecto si se menciona; 0 si no)
        - yardas: number             (ESTIMA yardas cúbicas de concreto según lotes/unidades/sqft)
        - empresaContacto: string | null  (el DESARROLLADOR/empresa del proyecto)
        - nombreContacto: string | null
        - email: string | null
        - telefono: string | null
        - telefonoEmpresa: string | null
        - fuente: string | null      (el SOURCE_URL o el nombre del sitio)
        - prioridad: number          (0-100: qué tan buen prospecto es para vender concreto)
        - nota: string | null        (1 frase: gancho comercial + que requiere verificación)

        REGLAS ESTRICTAS:
        1. Incluye SOLO proyectos de construcción REALES y específicos (una comunidad, un
           desarrollo, un edificio, un parque industrial, etc.). Ignora páginas genéricas,
           listados inmobiliarios, directorios o resultados sin un proyecto concreto.
        2. NO inventes nada. email/telefono/nombreContacto SOLO si aparecen en el texto
           (típicamente en PAGE_TEXT). Si no están, pon null. Nunca fabriques contactos.
        3. 'yardas' y 'prioridad' SÍ los estimas con criterio (más lotes/unidades/valor y fase
           más temprana = mayor prioridad y más yardas).
        4. 'claveExterna' SIEMPRE es el SOURCE_URL del bloque de donde sacaste el proyecto.
        5. Si no hay proyectos utilizables, devuelve [].
        6. Responde con JSON válido y nada más.
        """;
}
