using System.Text.Json;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// "Enriquecedor" de paso directo: la fuente de datos abiertos ya entrega leads normalizados
/// (un arreglo JSON de <see cref="ProyectoLead"/>), así que aquí solo se deserializan. Sin IA,
/// sin costo. Se usa cuando Leads:Fuente:Proveedor = "DatosAbiertos".
/// </summary>
public class EnriquecedorDirecto : IEnriquecedorProyectosIa
{
    private readonly ILogger<EnriquecedorDirecto> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EnriquecedorDirecto(ILogger<EnriquecedorDirecto> logger) => _logger = logger;

    public bool EstaConfigurado => true;

    public Task<IReadOnlyList<ProyectoLead>> NormalizarAsync(
        string datosCrudos, CriteriosBusqueda criterios, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(datosCrudos))
            return Task.FromResult<IReadOnlyList<ProyectoLead>>(Array.Empty<ProyectoLead>());

        var leads = JsonSerializer.Deserialize<List<ProyectoLead>>(datosCrudos, JsonOpts) ?? new();
        var validos = leads.Where(l => !string.IsNullOrWhiteSpace(l.NombreProyecto)).ToList();
        _logger.LogInformation("Enriquecedor directo: {N} lead(s) de datos abiertos.", validos.Count);
        return Task.FromResult<IReadOnlyList<ProyectoLead>>(validos);
    }
}
