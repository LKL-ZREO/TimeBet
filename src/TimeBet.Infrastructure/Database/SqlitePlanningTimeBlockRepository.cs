using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Tasks;

namespace TimeBet.Infrastructure.Database;

public sealed class SqlitePlanningTimeBlockRepository : IPlanningTimeBlockRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePlanningTimeBlockRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PlanningTimeBlock>> GetInRangeAsync(
        DateTimeOffset startsAtOrAfterUtc,
        DateTimeOffset startsBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + "\n" + """
            WHERE starts_at_utc_ms >= $startsAtOrAfter
              AND starts_at_utc_ms < $startsBefore
            ORDER BY starts_at_utc_ms, id;
            """;
        command.Parameters.AddWithValue("$startsAtOrAfter", ToMilliseconds(startsAtOrAfterUtc));
        command.Parameters.AddWithValue("$startsBefore", ToMilliseconds(startsBeforeUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var blocks = new List<PlanningTimeBlock>();
        while (await reader.ReadAsync(cancellationToken))
        {
            blocks.Add(Read(reader));
        }

        return blocks;
    }

    public async Task<PlanningTimeBlock?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<bool> HasOverlapAsync(
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM planning_time_blocks
                WHERE status <> 'skipped'
                  AND ($excludeId IS NULL OR id <> $excludeId)
                  AND starts_at_utc_ms < $endsAt
                  AND starts_at_utc_ms + planned_seconds * 1000 > $startsAt
            );
            """;
        command.Parameters.AddWithValue("$startsAt", ToMilliseconds(startsAtUtc));
        command.Parameters.AddWithValue("$endsAt", ToMilliseconds(endsAtUtc));
        command.Parameters.AddWithValue("$excludeId", excludeId.HasValue
            ? excludeId.Value.ToString("D")
            : DBNull.Value);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    public async Task<bool> HasPlannedForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM planning_time_blocks
                WHERE task_id = $taskId AND status = 'planned'
            );
            """;
        command.Parameters.AddWithValue("$taskId", taskId.ToString("D"));
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    public async Task AddAsync(
        PlanningTimeBlock block,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planning_time_blocks
                (id, task_id, starts_at_utc_ms, planned_seconds, actual_seconds,
                 status, created_at_utc_ms, resolved_at_utc_ms)
            VALUES
                ($id, $taskId, $startsAt, $plannedSeconds, 0, 'planned', $createdAt, NULL);
            """;
        command.Parameters.AddWithValue("$id", block.Id.ToString("D"));
        command.Parameters.AddWithValue("$taskId", block.TaskId.ToString("D"));
        command.Parameters.AddWithValue("$startsAt", ToMilliseconds(block.StartsAtUtc));
        command.Parameters.AddWithValue("$plannedSeconds", block.PlannedSeconds);
        command.Parameters.AddWithValue("$createdAt", ToMilliseconds(block.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MoveAsync(
        Guid id,
        DateTimeOffset startsAtUtc,
        int plannedSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE planning_time_blocks
            SET starts_at_utc_ms = $startsAt,
                planned_seconds = $plannedSeconds
            WHERE id = $id
              AND status = 'planned'
              AND NOT EXISTS (
                  SELECT 1
                  FROM study_sessions
                  WHERE time_block_id = $id AND active_slot = 1
              );
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$startsAt", ToMilliseconds(startsAtUtc));
        command.Parameters.AddWithValue("$plannedSeconds", plannedSeconds);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The time block is active or has already changed.");
        }
    }

    public async Task SkipAsync(
        Guid id,
        DateTimeOffset skippedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE planning_time_blocks
            SET status = 'skipped', resolved_at_utc_ms = $resolvedAt
            WHERE id = $id
              AND status = 'planned'
              AND NOT EXISTS (
                  SELECT 1
                  FROM study_sessions
                  WHERE time_block_id = $id AND active_slot = 1
              );
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$resolvedAt", ToMilliseconds(skippedAtUtc));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The time block is active or has already changed.");
        }
    }

    private const string BaseSelect = """
        SELECT id, task_id, starts_at_utc_ms, planned_seconds, actual_seconds,
               status, created_at_utc_ms, resolved_at_utc_ms
        FROM planning_time_blocks
        """;

    private static PlanningTimeBlock Read(SqliteDataReader reader)
    {
        return PlanningTimeBlock.Restore(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            FromMilliseconds(reader.GetInt64(2)),
            reader.GetInt32(3),
            reader.GetInt64(4),
            Enum.Parse<PlanningTimeBlockStatus>(reader.GetString(5), ignoreCase: true),
            FromMilliseconds(reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : FromMilliseconds(reader.GetInt64(7)));
    }

    private static long ToMilliseconds(DateTimeOffset value) =>
        SqliteValueConverters.ToUnixMilliseconds(value);

    private static DateTimeOffset FromMilliseconds(long value) =>
        SqliteValueConverters.FromUnixMilliseconds(value);
}
