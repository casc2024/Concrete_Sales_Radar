using Npgsql;

namespace ConcreteSalesRadar.Data;

public static class ConnectionStringHelper
{
    /// <summary>
    /// Acepta tanto una cadena nativa de Npgsql ("Host=...;Port=...;...") como una
    /// URL estilo Railway/Heroku ("postgresql://usuario:pass@host:puerto/base") y
    /// devuelve siempre una cadena válida para Npgsql. Railway inyecta DATABASE_URL
    /// en formato URL, por eso se normaliza aquí.
    /// </summary>
    public static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException("No se configuró la cadena de conexión a PostgreSQL.");

        valor = valor.Trim();

        if (!valor.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !valor.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return valor; // Ya es formato Npgsql.
        }

        var uri = new Uri(valor);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Prefer
        };

        return builder.ConnectionString;
    }
}
