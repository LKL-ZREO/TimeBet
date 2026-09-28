using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Application.AI;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteAiSettingsRepository : IAiSettingsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteAiSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<StoredAiSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT enabled, base_url, model, api_key_protected
            FROM ai_settings
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("AI settings were not initialized.");
        }

        return new StoredAiSettings(
            reader.GetBoolean(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : WindowsUserSecretProtector.Unprotect((byte[])reader[3]));
    }

    public async Task SaveAsync(
        StoredAiSettings settings,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ai_settings
            SET enabled = $enabled,
                base_url = $baseUrl,
                model = $model,
                api_key_protected = $apiKey,
                updated_at_utc_ms = $updatedAt
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$enabled", settings.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$baseUrl", settings.BaseUrl);
        command.Parameters.AddWithValue("$model", settings.Model);
        command.Parameters.AddWithValue(
            "$apiKey",
            string.IsNullOrWhiteSpace(settings.ApiKey)
                ? DBNull.Value
                : WindowsUserSecretProtector.Protect(settings.ApiKey));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("AI settings could not be saved.");
        }
    }
}
