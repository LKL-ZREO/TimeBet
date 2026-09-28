using TimeBet.Application.Abstractions;
using TimeBet.Application.Analytics;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteAnalyticsRepository : IAnalyticsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteAnalyticsRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AnalyticsStudySessionRecord>> GetStudySessionsAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.goal, e.event_type, e.occurred_at_utc_ms
            FROM study_sessions AS s
            INNER JOIN session_events AS e ON e.session_id = s.id
            WHERE s.started_at_utc_ms < $startedBefore
              AND COALESCE(s.ended_at_utc_ms, $startedBefore) > $endedAfter
            ORDER BY s.started_at_utc_ms, s.id, e.sequence;
            """;
        command.Parameters.AddWithValue("$startedBefore", ToMilliseconds(startedBeforeUtc));
        command.Parameters.AddWithValue("$endedAfter", ToMilliseconds(endedAfterUtc));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<SessionBuilder>();
        SessionBuilder? current = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            if (current?.Id != id)
            {
                current = new SessionBuilder(id, reader.GetString(1));
                sessions.Add(current);
            }

            current.Events.Add(new AnalyticsSessionEventRecord(
                SqliteValueConverters.ToSessionEventType(reader.GetString(2)),
                FromMilliseconds(reader.GetInt64(3))));
        }

        return sessions.Select(session => new AnalyticsStudySessionRecord(
            session.Id,
            session.Goal,
            session.Events)).ToArray();
    }

    public async Task<IReadOnlyList<AnalyticsActivityRecord>> GetActivitiesAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT started_at_utc_ms, ended_at_utc_ms, process_name, is_idle
            FROM activity_segments
            WHERE started_at_utc_ms < $startedBefore
              AND ended_at_utc_ms > $endedAfter
            ORDER BY started_at_utc_ms;
            """;
        command.Parameters.AddWithValue("$startedBefore", ToMilliseconds(startedBeforeUtc));
        command.Parameters.AddWithValue("$endedAfter", ToMilliseconds(endedAfterUtc));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activities = new List<AnalyticsActivityRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            activities.Add(new AnalyticsActivityRecord(
                FromMilliseconds(reader.GetInt64(0)),
                FromMilliseconds(reader.GetInt64(1)),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return activities;
    }

    public async Task<LedgerTotalsRecord> GetLedgerTotalsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(amount_seconds), 0),
                   COALESCE(SUM(CASE WHEN amount_seconds > 0 THEN amount_seconds ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN amount_seconds < 0 THEN -amount_seconds ELSE 0 END), 0)
            FROM ledger_entries;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new LedgerTotalsRecord(0, 0, 0);
        }

        return new LedgerTotalsRecord(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    public async Task<IReadOnlyList<LedgerEntrySnapshot>> GetLedgerEntriesAsync(
        int? count,
        CancellationToken cancellationToken = default)
    {
        if (count is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, entry_type, amount_seconds, source_type, source_id, note, occurred_at_utc_ms
            FROM ledger_entries
            ORDER BY occurred_at_utc_ms DESC, id DESC
            """ + (count.HasValue ? " LIMIT $count;" : ";");
        if (count.HasValue)
        {
            command.Parameters.AddWithValue("$count", count.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<LedgerEntrySnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new LedgerEntrySnapshot(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                FromMilliseconds(reader.GetInt64(6))));
        }

        return entries;
    }

    private static long ToMilliseconds(DateTimeOffset value) =>
        SqliteValueConverters.ToUnixMilliseconds(value);

    private static DateTimeOffset FromMilliseconds(long value) =>
        SqliteValueConverters.FromUnixMilliseconds(value);

    private sealed record SessionBuilder(Guid Id, string Goal)
    {
        public List<AnalyticsSessionEventRecord> Events { get; } = [];
    }
}
