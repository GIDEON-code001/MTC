using Npgsql;

namespace BodaBoss.Api.Data;

public static class DatabaseSchemaInitializer
{
    private const long InitializationLockId = 824735901;

    public static async Task InitializeIfEmptyAsync(
        string connectionString,
        string schemaPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException("The initial database schema file was not found.", schemaPath);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var acquireLock = new NpgsqlCommand(
            "SELECT pg_advisory_lock(@lockId)", connection))
        {
            acquireLock.Parameters.AddWithValue("lockId", InitializationLockId);
            await acquireLock.ExecuteScalarAsync(cancellationToken);
        }

        try
        {
            await using var schemaExists = new NpgsqlCommand(
                "SELECT to_regclass('public.tenants') IS NOT NULL", connection);
            var isInitialized = (bool)(await schemaExists.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not check the database schema state."));
            if (isInitialized)
            {
                return;
            }

            var schema = await File.ReadAllTextAsync(schemaPath, cancellationToken);
            await using var initialize = new NpgsqlCommand(schema, connection)
            {
                CommandTimeout = 120
            };
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await using var releaseLock = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@lockId)", connection);
            releaseLock.Parameters.AddWithValue("lockId", InitializationLockId);
            await releaseLock.ExecuteScalarAsync(CancellationToken.None);
        }
    }
}
