using Npgsql;

namespace BodaBoss.Api.Data;

public static class PostgresConnectionString
{
    public static string FromConfiguration(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("Postgres")
            ?? configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres or DATABASE_URL must be configured.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return value;
        }

        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
        {
            throw new InvalidOperationException("DATABASE_URL must include a database name and credentials.");
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            SslMode = SslMode.Require
        };
        return connection.ConnectionString;
    }
}
