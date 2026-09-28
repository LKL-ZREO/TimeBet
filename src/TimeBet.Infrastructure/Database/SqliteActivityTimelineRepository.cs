using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Timeline;
using TimeBet.Domain.Timeline;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteActivityTimelineRepository : IActivityTimelineRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteActivityTimelineRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AppendOrMergeActivityAsync(
        ActivitySegment segment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var activeCheck = connection.CreateCommand())
        {
            activeCheck.Transaction = transaction;
            activeCheck.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM study_sessions
                    WHERE id = $sessionId AND active_slot = 1 AND status = 'running'
                );
                """;
            activeCheck.Parameters.AddWithValue("$sessionId", segment.SessionId.ToString("D"));
            if (Convert.ToInt64(await activeCheck.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
        }

        await using var merge = connection.CreateCommand();
        merge.Transaction = transaction;
        merge.CommandText = """
            UPDATE activity_segments
            SET ended_at_utc_ms = $endedAt
            WHERE id = (
                SELECT id
                FROM activity_segments
                WHERE session_id = $sessionId
                  AND process_name = $processName
                  AND window_title = $windowTitle
                  AND is_idle = $isIdle
                  AND ended_at_utc_ms BETWEEN $mergeAfter AND $startedAt
                ORDER BY ended_at_utc_ms DESC
                LIMIT 1
            );
            """;
        merge.Parameters.AddWithValue("$endedAt", ToMilliseconds(segment.EndedAtUtc));
        merge.Parameters.AddWithValue("$sessionId", segment.SessionId.ToString("D"));
        merge.Parameters.AddWithValue("$processName", segment.ProcessName);
        merge.Parameters.AddWithValue("$windowTitle", segment.WindowTitle);
        merge.Parameters.AddWithValue("$isIdle", segment.IsIdle ? 1 : 0);
        merge.Parameters.AddWithValue("$mergeAfter", ToMilliseconds(segment.StartedAtUtc.AddSeconds(-2)));
        merge.Parameters.AddWithValue("$startedAt", ToMilliseconds(segment.StartedAtUtc));

        if (await merge.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO activity_segments
                    (id, session_id, started_at_utc_ms, ended_at_utc_ms, process_name,
                     window_title, is_idle, document_name, document_type)
                VALUES
                    ($id, $sessionId, $startedAt, $endedAt, $processName,
                     $windowTitle, $isIdle, $documentName, $documentType);
                """;
            insert.Parameters.AddWithValue("$id", segment.Id.ToString("D"));
            insert.Parameters.AddWithValue("$sessionId", segment.SessionId.ToString("D"));
            insert.Parameters.AddWithValue("$startedAt", ToMilliseconds(segment.StartedAtUtc));
            insert.Parameters.AddWithValue("$endedAt", ToMilliseconds(segment.EndedAtUtc));
            insert.Parameters.AddWithValue("$processName", segment.ProcessName);
            insert.Parameters.AddWithValue("$windowTitle", segment.WindowTitle);
            insert.Parameters.AddWithValue("$isIdle", segment.IsIdle ? 1 : 0);
            insert.Parameters.AddWithValue("$documentName", (object?)segment.DocumentName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$documentType", (object?)segment.DocumentType ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddScreenshotAsync(
        ScreenshotRecord screenshot,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO screenshot_records
                (id, session_id, activity_segment_id, captured_at_utc_ms, file_path,
                 thumbnail_path, width, height, file_size_bytes)
            VALUES
                ($id, $sessionId, $activityId, $capturedAt, $filePath,
                 $thumbnailPath, $width, $height, $fileSize);
            """;
        command.Parameters.AddWithValue("$id", screenshot.Id.ToString("D"));
        command.Parameters.AddWithValue("$sessionId", screenshot.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$activityId", screenshot.ActivitySegmentId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$capturedAt", ToMilliseconds(screenshot.CapturedAtUtc));
        command.Parameters.AddWithValue("$filePath", screenshot.FilePath);
        command.Parameters.AddWithValue("$thumbnailPath", screenshot.ThumbnailPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$width", screenshot.Width);
        command.Parameters.AddWithValue("$height", screenshot.Height);
        command.Parameters.AddWithValue("$fileSize", screenshot.FileSizeBytes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TimelineSessionRecord>> GetSessionsAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var headers = new List<SessionHeader>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, goal, status, started_at_utc_ms, ended_at_utc_ms,
                       COALESCE(
                           effective_duration_seconds,
                           MAX(0, CAST((
                               $asOf - started_at_utc_ms - accumulated_paused_seconds * 1000 -
                               CASE
                                   WHEN status = 'paused' THEN $asOf - paused_at_utc_ms
                                   ELSE 0
                               END
                           ) / 1000 AS INTEGER)))
                FROM study_sessions
                WHERE started_at_utc_ms < $startedBefore
                  AND COALESCE(ended_at_utc_ms, $startedBefore) > $endedAfter
                ORDER BY started_at_utc_ms DESC;
                """;
            command.Parameters.AddWithValue("$startedBefore", ToMilliseconds(startedBeforeUtc));
            command.Parameters.AddWithValue("$endedAfter", ToMilliseconds(endedAfterUtc));
            command.Parameters.AddWithValue("$asOf", Math.Min(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ToMilliseconds(startedBeforeUtc)));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                headers.Add(new SessionHeader(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    FromMilliseconds(reader.GetInt64(3)),
                    reader.IsDBNull(4) ? null : FromMilliseconds(reader.GetInt64(4)),
                    reader.GetInt64(5)));
            }
        }

        var sessions = new List<TimelineSessionRecord>(headers.Count);
        foreach (var header in headers)
        {
            sessions.Add(new TimelineSessionRecord(
                header.Id,
                header.Goal,
                header.Status,
                header.StartedAtUtc,
                header.EndedAtUtc,
                header.EffectiveDurationSeconds,
                await GetActivitiesAsync(connection, header.Id, startedBeforeUtc, endedAfterUtc, cancellationToken),
                await GetScreenshotsAsync(connection, header.Id, startedBeforeUtc, endedAfterUtc, cancellationToken)));
        }

        return sessions;
    }

    public async Task<IReadOnlyList<string>> DeleteScreenshotsBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var paths = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT file_path FROM screenshot_records WHERE captured_at_utc_ms < $cutoff;";
            select.Parameters.AddWithValue("$cutoff", ToMilliseconds(cutoffUtc));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                paths.Add(reader.GetString(0));
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM screenshot_records WHERE captured_at_utc_ms < $cutoff;";
            delete.Parameters.AddWithValue("$cutoff", ToMilliseconds(cutoffUtc));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return paths;
    }

    private static async Task<IReadOnlyList<TimelineActivityRecord>> GetActivitiesAsync(
        SqliteConnection connection,
        Guid sessionId,
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken)
    {
        var activities = new List<TimelineActivityRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   MAX(started_at_utc_ms, $endedAfter),
                   MIN(ended_at_utc_ms, $startedBefore),
                   process_name, window_title, is_idle
            FROM activity_segments
            WHERE session_id = $sessionId
              AND started_at_utc_ms < $startedBefore
              AND ended_at_utc_ms > $endedAfter
            ORDER BY started_at_utc_ms;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$startedBefore", ToMilliseconds(startedBeforeUtc));
        command.Parameters.AddWithValue("$endedAfter", ToMilliseconds(endedAfterUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            activities.Add(new TimelineActivityRecord(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                FromMilliseconds(reader.GetInt64(1)),
                FromMilliseconds(reader.GetInt64(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5)));
        }

        return activities;
    }

    private static async Task<IReadOnlyList<TimelineScreenshotRecord>> GetScreenshotsAsync(
        SqliteConnection connection,
        Guid sessionId,
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken)
    {
        var screenshots = new List<TimelineScreenshotRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, captured_at_utc_ms, file_path, width, height, file_size_bytes
            FROM screenshot_records
            WHERE session_id = $sessionId
              AND captured_at_utc_ms >= $endedAfter
              AND captured_at_utc_ms < $startedBefore
            ORDER BY captured_at_utc_ms;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$startedBefore", ToMilliseconds(startedBeforeUtc));
        command.Parameters.AddWithValue("$endedAfter", ToMilliseconds(endedAfterUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            screenshots.Add(new TimelineScreenshotRecord(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                FromMilliseconds(reader.GetInt64(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt64(5)));
        }

        return screenshots;
    }

    private static long ToMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

    private sealed record SessionHeader(
        Guid Id,
        string Goal,
        string Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        long EffectiveDurationSeconds);
}
