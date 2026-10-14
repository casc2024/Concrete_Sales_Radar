namespace ConcreteSalesRadar.Services.Config;

/// <summary>
/// Elige la estrategia de configuración según el entorno de ASP.NET Core. En desarrollo
/// (ASPNETCORE_ENVIRONMENT=Development) usa <see cref="ConfiguracionLocal"/>; en cualquier otro
/// caso —incluido Railway, que corre como Production por defecto— usa
/// <see cref="ConfiguracionProduccion"/>.
/// </summary>
public static class SelectorConfiguracionEntorno
{
    public static IConfiguracionEntorno Crear(IHostEnvironment entorno, IConfiguration config) =>
        entorno.IsDevelopment()
            ? new ConfiguracionLocal(config)
            : new ConfiguracionProduccion(config);
}
