namespace ConcreteSalesRadar.Services.Config;

/// <summary>
/// Estrategia que resuelve los secretos sensibles (cadena de conexión y API keys) según el
/// entorno de ejecución. En local salen de appsettings.Development.json / user-secrets; en
/// producción (Railway) salen de variables de entorno. El resto del arranque no sabe de dónde
/// vienen: solo pide a la estrategia activa.
/// </summary>
public interface IConfiguracionEntorno
{
    /// <summary>Nombre legible de la estrategia (se registra en el log de arranque).</summary>
    string Nombre { get; }

    /// <summary>Cadena de conexión cruda a PostgreSQL (aún sin normalizar).</summary>
    string? ObtenerCadenaConexion();

    /// <summary>API key de Shovels (fuente de permisos de construcción), o null si no está.</summary>
    string? ObtenerShovelsApiKey();

    /// <summary>API key de Anthropic/Claude para normalizar leads, o null si no está.</summary>
    string? ObtenerIaApiKey();
}
