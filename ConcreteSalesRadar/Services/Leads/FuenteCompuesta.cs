using System.Text.Json;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Fuente compuesta: corre VARIAS fuentes en una sola pasada y fusiona sus leads ya normalizados.
/// Permite cobertura nacional combinando lo gratis (TDLR TABS + Socrata + ArcGIS + SAM.gov).
/// Cada sub-fuente ya devuelve un arreglo JSON de ProyectoLead; aquí se concatenan.
/// Se activa con Leads:Fuente:Proveedor = "Nacional".
/// </summary>
public class FuenteCompuesta : IFuenteProyectos
{
    private readonly IReadOnlyList<(string nombre, IFuenteProyectos fuente)> _fuentes;
    private readonly ILogger<FuenteCompuesta> _logger;

    public FuenteCompuesta(IReadOnlyList<(string, IFuenteProyectos)> fuentes, ILogger<FuenteCompuesta> logger)
    {
        _fuentes = fuentes;
        _logger = logger;
    }

    public bool EstaConfigurada => _fuentes.Any(f => f.fuente.EstaConfigurada);

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var combinados = new List<JsonElement>();

        foreach (var (nombre, fuente) in _fuentes)
        {
            if (!fuente.EstaConfigurada)
            {
                _logger.LogInformation("Fuente {Nombre} sin configurar, se omite.", nombre);
                continue;
            }
            try
            {
                var crudo = await fuente.ObtenerDatosCrudosAsync(criterios, ct);
                if (string.IsNullOrWhiteSpace(crudo)) continue;
                using var doc = JsonDocument.Parse(crudo);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var el in doc.RootElement.EnumerateArray())
                        combinados.Add(el.Clone());
                _logger.LogInformation("Fuente {Nombre}: aportó datos.", nombre);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fuente {Nombre} falló; se continúa con las demás.", nombre);
            }
        }

        return JsonSerializer.Serialize(combinados);
    }
}
