namespace ConcreteSalesRadar.Services.Config;

/// <summary>
/// Estrategia de entorno PRODUCCIÓN (Railway). Los secretos salen de variables de entorno;
/// si alguna falta, cae a la configuración de .NET como respaldo. Así, al publicar en Railway
/// solo hay que definir las variables y nada sensible vive en el repositorio.
///
/// Variables esperadas en Railway:
///   - DATABASE_URL            (la inyecta Railway al enlazar la base PostgreSQL)
///   - SHOVELS_API_KEY  o  Leads__Fuente__ApiKey
///   - ANTHROPIC_API_KEY  o  Leads__Ia__ApiKey
/// </summary>
public class ConfiguracionProduccion : IConfiguracionEntorno
{
    private readonly IConfiguration _config;

    public ConfiguracionProduccion(IConfiguration config) => _config = config;

    public string Nombre => "Producción (variables de entorno / Railway)";

    public string? ObtenerCadenaConexion() =>
        Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? _config.GetConnectionString("Flota");

    public string? ObtenerShovelsApiKey() =>
        Environment.GetEnvironmentVariable("SHOVELS_API_KEY")
        ?? Environment.GetEnvironmentVariable("Leads__Fuente__ApiKey")
        ?? _config["Leads:Fuente:ApiKey"];

    public string? ObtenerIaApiKey() =>
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        ?? Environment.GetEnvironmentVariable("Leads__Ia__ApiKey")
        ?? _config["Leads:Ia:ApiKey"];
}
