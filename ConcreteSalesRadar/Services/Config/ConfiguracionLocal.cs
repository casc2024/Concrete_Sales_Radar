namespace ConcreteSalesRadar.Services.Config;

/// <summary>
/// Estrategia de entorno LOCAL (desarrollo). Los secretos salen de la configuración de .NET:
/// appsettings.Development.json y user-secrets. Pensada para probar el dashboard protegido de
/// Hangfire en la máquina del desarrollador.
///
/// Para apuntar a un PostgreSQL local (en vez de la base de Railway) basta con agregar
/// "ConnectionStrings:Local" en appsettings.Development.json; si no existe, usa "Flota".
/// </summary>
public class ConfiguracionLocal : IConfiguracionEntorno
{
    private readonly IConfiguration _config;

    public ConfiguracionLocal(IConfiguration config) => _config = config;

    public string Nombre => "Local (appsettings.Development / user-secrets)";

    public string? ObtenerCadenaConexion() =>
        _config.GetConnectionString("Local") ?? _config.GetConnectionString("Flota");

    public string? ObtenerShovelsApiKey() =>
        _config["Leads:Fuente:ApiKey"];

    public string? ObtenerIaApiKey() =>
        _config["Leads:Ia:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
}
