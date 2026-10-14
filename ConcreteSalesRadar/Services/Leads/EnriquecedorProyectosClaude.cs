using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Normaliza y prioriza los leads usando Claude. El modelo recibe el JSON crudo del
/// proveedor y lo reestructura a nuestros campos; además estima yardas de concreto y una
/// prioridad comercial. Regla dura: no inventa contactos, emails ni teléfonos.
/// </summary>
public class EnriquecedorProyectosClaude : IEnriquecedorProyectosIa
{
    private readonly IaOpciones _opciones;
    private readonly ILogger<EnriquecedorProyectosClaude> _logger;
    private readonly string? _apiKey;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public EnriquecedorProyectosClaude(IOptions<OpcionesLeads> opciones, ILogger<EnriquecedorProyectosClaude> logger)
    {
        _opciones = opciones.Value.Ia;
        _logger = logger;
        _apiKey = !string.IsNullOrWhiteSpace(_opciones.ApiKey)
            ? _opciones.ApiKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<ProyectoLead>> NormalizarAsync(
        string datosCrudos, CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (!EstaConfigurado) return Array.Empty<ProyectoLead>();

        var client = new AnthropicClient { ApiKey = _apiKey };

        var mensajeUsuario =
            $"Criterios de búsqueda:\n" +
            $"- Ubicación: {criterios.Ubicacion}\n" +
            $"- Radio: {criterios.RadioMillas} millas\n" +
            $"- Tipo de proyecto: {criterios.TipoProyecto}\n" +
            $"- Presupuesto mínimo: {criterios.PresupuestoMinimo:C0}\n\n" +
            $"Datos crudos de la fuente (normalízalos):\n```\n{datosCrudos}\n```";

        try
        {
            var respuesta = await client.Messages.Create(new MessageCreateParams
            {
                Model = _opciones.Modelo,
                MaxTokens = 16000,
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = mensajeUsuario }]
            });

            var texto = string.Concat(
                respuesta.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

            var json = ExtraerArregloJson(texto);
            if (json is null)
            {
                // Respondió, pero sin JSON: es un fallo real, no un "sin resultados". Lo
                // propagamos para que Hangfire lo marque como Failed y se vea en el dashboard.
                _logger.LogWarning("La IA no devolvió un arreglo JSON reconocible.");
                throw new InvalidOperationException(
                    "La IA respondió sin un arreglo JSON reconocible. Respuesta: " + Recortar(texto, 300));
            }

            var leads = JsonSerializer.Deserialize<List<ProyectoLead>>(json, JsonOpts) ?? new();
            // Origen Shovels (API comercial de construcción).
            foreach (var l in leads) l.EstadoVerificacion = Models.Entities.EstadosVerificacion.Comercial;
            // Descarta registros vacíos (sin nombre de proyecto).
            return leads.Where(l => !string.IsNullOrWhiteSpace(l.NombreProyecto)).ToList();
        }
        catch (Exception ex)
        {
            // No tragar el error: que la pasada falle visiblemente (Failed + stack en Hangfire,
            // con reintentos), en vez de terminar en verde con "Sin leads".
            _logger.LogError(ex, "Error al normalizar leads con la IA");
            throw new InvalidOperationException(
                "Fallo al llamar a la IA (Claude) para normalizar leads: " + ex.Message, ex);
        }
    }

    private static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max] + "…";

    /// <summary>Extrae el primer arreglo JSON del texto (por si el modelo agrega prosa).</summary>
    private static string? ExtraerArregloJson(string texto)
    {
        var inicio = texto.IndexOf('[');
        var fin = texto.LastIndexOf(']');
        return (inicio >= 0 && fin > inicio) ? texto[inicio..(fin + 1)] : null;
    }

    private const string SystemPrompt = """
        Eres un asistente que normaliza datos de proyectos de construcción para una
        herramienta comercial de venta de concreto premezclado (ready-mix) en North Texas.

        Recibes datos CRUDOS de una API real de construcción y debes devolver SOLO un
        arreglo JSON (sin texto adicional, sin ```), donde cada elemento tiene estas claves:

        - claveExterna: string | null  (id único del proyecto/permiso en la fuente, para no duplicar)
        - direccion: string | null
        - nombreProyecto: string       (obligatorio)
        - tipo: string | null          (ej. Commercial, Residential, Infrastructure)
        - fechaInicio: string | null   (ISO 8601, ej. "2026-05-01", o null)
        - presupuesto: number          (USD, 0 si no se conoce)
        - yardas: number               (yardas cúbicas de concreto estimadas; entero)
        - empresaContacto: string | null
        - nombreContacto: string | null
        - email: string | null
        - telefono: string | null
        - telefonoEmpresa: string | null
        - fuente: string | null        (nombre de la fuente de los datos)
        - prioridad: number            (0-100, qué tan buen prospecto es para vender concreto)
        - nota: string | null          (1 frase con el gancho comercial)

        MAPEO DE LA FUENTE (puede venir de la API de permisos Shovels u otra):
        - Los montos de dinero (ej. 'job_value', '*_value') suelen venir EN CENTAVOS:
          divídelos entre 100 para 'presupuesto' en dólares.
        - La dirección puede venir como objeto (street_no, street, city, state, zip_code):
          compón 'direccion' como un string legible (ej. "5400 Westport Pkwy, Fort Worth, TX").
        - 'id'/'number'/'permit_id' de la fuente -> 'claveExterna'.
        - 'description'/'description_derived' -> 'nombreProyecto' (si no hay nombre propio).
        - 'property_legal_owner' -> 'empresaContacto' (o 'nombreContacto' si es una persona).
        - 'type'/'property_type' -> 'tipo'. Fechas (start_date/issue_date/file_date) -> 'fechaInicio'.
        - 'jurisdiction'/fuente de los datos -> 'fuente'.

        REGLAS ESTRICTAS:
        1. NO inventes datos. Usa únicamente valores presentes en los datos crudos. Si un
           contacto, email o teléfono no está en la fuente, pon null. Nunca fabriques
           nombres de personas, correos ni números de teléfono. (Los permisos de Shovels
           normalmente NO traen email ni teléfono: déjalos en null.)
        2. 'yardas' y 'prioridad' SÍ los puedes estimar con criterio (yardas de concreto a
           partir del tipo y presupuesto; prioridad 0-100 según tamaño y tipo de obra).
        3. Devuelve solo los proyectos que realmente vengan en los datos; no rellenes.
        4. Si los datos crudos no contienen proyectos utilizables, devuelve [].
        5. Responde con JSON válido y nada más.
        """;
}
