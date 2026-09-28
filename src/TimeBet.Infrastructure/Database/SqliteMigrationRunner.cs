using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteMigrationRunner
{
    private const string MigrationTable = "__timebet_migrations";
    private readonly Assembly assembly;

    public SqliteMigrationRunner(Assembly? assembly = null)
    {
        this.assembly = assembly ?? typeof(SqliteMigrationRunner).Assembly;
    }

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {MigrationTable} (
                    migration_id INTEGER PRIMARY KEY,
                    migration_name TEXT NOT NULL,
                    applied_at_utc_ms INTEGER NOT NULL
                );
                """;
            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migration in GetMigrations())
        {
            if (await IsAppliedAsync(connection, migration.Id, cancellationToken))
            {
                continue;
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (migration.Id == 3)
            {
                await EnsureSessionScreenshotConsentColumnAsync(connection, transaction, cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = await ReadMigrationAsync(migration.ResourceName, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = $"""
                INSERT INTO {MigrationTable} (migration_id, migration_name, applied_at_utc_ms)
                VALUES ($id, $name, $appliedAt);
                """;
            record.Parameters.AddWithValue("$id", migration.Id);
            record.Parameters.AddWithValue("$name", migration.Name);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await record.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task EnsureSessionScreenshotConsentColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.Transaction = transaction;
        exists.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pragma_table_info('study_sessions')
                WHERE name = 'capture_screenshots'
            );
            """;
        if (Convert.ToInt64(
            await exists.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1)
        {
            return;
        }

        await using var addColumn = connection.CreateCommand();
        addColumn.Transaction = transaction;
        addColumn.CommandText = """
            ALTER TABLE study_sessions
            ADD COLUMN capture_screenshots INTEGER NOT NULL DEFAULT 0
                CHECK (capture_screenshots IN (0, 1));
            """;
        await addColumn.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsAppliedAsync(
        SqliteConnection connection,
        int migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {MigrationTable} WHERE migration_id = $id);";
        command.Parameters.AddWithValue("$id", migrationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private IEnumerable<Migration> GetMigrations()
    {
        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                var migrationName = name[(name.LastIndexOf(".Migrations.", StringComparison.Ordinal) + ".Migrations.".Length)..];
                var separator = migrationName.IndexOf('_');
                if (separator <= 0 || !int.TryParse(migrationName[..separator], out var id))
                {
                    throw new InvalidOperationException($"Migration resource '{name}' must start with a numeric ID.");
                }

                return new Migration(id, migrationName, name);
            })
            .OrderBy(migration => migration.Id);
    }

    private async Task<string> ReadMigrationAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private sealed record Migration(int Id, string Name, string ResourceName);
}
