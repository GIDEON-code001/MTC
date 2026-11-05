using BodaBoss.Api.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace BodaBoss.Api.Tests;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void FromConfiguration_converts_postgres_url_for_render()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://fleet:p%40ss%3Aword@db.example.test:5433/bodaboss"
            })
            .Build();

        var result = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.FromConfiguration(configuration));

        Assert.Equal("db.example.test", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("bodaboss", result.Database);
        Assert.Equal("fleet", result.Username);
        Assert.Equal("p@ss:word", result.Password);
        Assert.Equal(SslMode.Require, result.SslMode);
    }

    [Fact]
    public void FromConfiguration_preserves_standard_npgsql_connection_strings()
    {
        const string connectionString =
            "Host=localhost;Port=5432;Database=bodaboss;Username=fleet;Password=test";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString
            })
            .Build();

        Assert.Equal(connectionString, PostgresConnectionString.FromConfiguration(configuration));
    }
}
