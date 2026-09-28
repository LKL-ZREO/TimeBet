using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Timeline;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteTrackingSettingsRepository : ITrackingSettingsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteTrackingSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<TrackingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT capture_activity, capture_screenshots, screenshot_interval_minutes,
                   screenshot_retention_days, idle_threshold_minutes
            FROM tracking_settings
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Tracking settings were not initialized.");
        }

        return new TrackingSettings(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
    }

    public async Task SaveAsync(TrackingSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tracking_settings
            SET capture_activity = $captureActivity,
                capture_screenshots = $captureScreenshots,
                screenshot_interval_minutes = $intervalMinutes,
                screenshot_retention_days = $retentionDays,
                idle_threshold_minutes = $idleMinutes,
                updated_at_utc_ms = $updatedAt
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$captureActivity", settings.CaptureActivity ? 1 : 0);
        command.Parameters.AddWithValue("$captureScreenshots", settings.CaptureScreenshots ? 1 : 0);
        command.Parameters.AddWithValue("$intervalMinutes", settings.ScreenshotIntervalMinutes);
        command.Parameters.AddWithValue("$retentionDays", settings.ScreenshotRetentionDays);
        command.Parameters.AddWithValue("$idleMinutes", settings.IdleThresholdMinutes);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Tracking settings could not be saved.");
        }
    }
}
