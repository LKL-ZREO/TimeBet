using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Sessions;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteStudySessionRepository : IStudySessionRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteStudySessionRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AddAsync(SessionTransition started, CancellationToken cancellationToken = default)
    {
        if (started.Event.Type != SessionEventType.Started || started.Session.Status != StudySessionStatus.Running)
        {
            throw new ArgumentException("The initial transition must start a running session.", nameof(started));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO study_sessions
                (id, goal, task_id, time_block_id, capture_screenshots, status, started_at_utc_ms, last_transition_at_utc_ms,
                     paused_at_utc_ms, ended_at_utc_ms, accumulated_paused_seconds,
                     effective_duration_seconds, completion_note, next_event_sequence, active_slot)
                VALUES
                    ($id, $goal, $taskId, $timeBlockId, $captureScreenshots, $status, $startedAt, $lastTransitionAt,
                     NULL, NULL, $pausedSeconds, NULL, NULL, $nextSequence, 1);
                """;
            AddSessionParameters(command, started.Session);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertEventAsync(connection, transaction, started.Event, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, goal, task_id, time_block_id, capture_screenshots, status, started_at_utc_ms, last_transition_at_utc_ms,
                   paused_at_utc_ms, ended_at_utc_ms, accumulated_paused_seconds,
                   effective_duration_seconds, completion_note, next_event_sequence
            FROM study_sessions
            WHERE active_slot = 1
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task SaveTransitionAsync(SessionTransition transition, CancellationToken cancellationToken = default)
    {
        if (transition.Event.Type is not (SessionEventType.Paused or SessionEventType.Resumed))
        {
            throw new ArgumentException("Only pause and resume transitions can be saved here.", nameof(transition));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE study_sessions
                SET status = $status,
                    last_transition_at_utc_ms = $lastTransitionAt,
                    paused_at_utc_ms = $pausedAt,
                    accumulated_paused_seconds = $pausedSeconds,
                    next_event_sequence = $nextSequence
                WHERE id = $id AND active_slot = 1 AND status IN ('running', 'paused');
                """;
            command.Parameters.AddWithValue("$status", transition.Session.Status.ToDatabaseValue());
            command.Parameters.AddWithValue("$lastTransitionAt", SqliteValueConverters.ToUnixMilliseconds(transition.Session.LastTransitionAtUtc));
            command.Parameters.AddWithValue("$pausedAt", transition.Session.PausedAtUtc.HasValue
                ? SqliteValueConverters.ToUnixMilliseconds(transition.Session.PausedAtUtc.Value)
                : DBNull.Value);
            command.Parameters.AddWithValue("$pausedSeconds", transition.Session.AccumulatedPausedSeconds);
            command.Parameters.AddWithValue("$nextSequence", transition.Session.NextEventSequence);
            command.Parameters.AddWithValue("$id", transition.Session.Id.ToString("D"));

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The active study session was changed by another operation.");
            }
        }

        await InsertEventAsync(connection, transaction, transition.Event, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteAndCreditAsync(
        StudySessionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE study_sessions
                SET status = 'completed',
                    last_transition_at_utc_ms = $lastTransitionAt,
                    paused_at_utc_ms = NULL,
                    ended_at_utc_ms = $endedAt,
                    accumulated_paused_seconds = $pausedSeconds,
                    effective_duration_seconds = $effectiveSeconds,
                    completion_note = $completionNote,
                    next_event_sequence = $nextSequence,
                    active_slot = NULL
                WHERE id = $id AND active_slot = 1 AND status IN ('running', 'paused');
                """;
            command.Parameters.AddWithValue("$lastTransitionAt", SqliteValueConverters.ToUnixMilliseconds(completion.Session.LastTransitionAtUtc));
            command.Parameters.AddWithValue("$endedAt", SqliteValueConverters.ToUnixMilliseconds(completion.Session.EndedAtUtc!.Value));
            command.Parameters.AddWithValue("$pausedSeconds", completion.Session.AccumulatedPausedSeconds);
            command.Parameters.AddWithValue("$effectiveSeconds", completion.Session.EffectiveDurationSeconds!.Value);
            command.Parameters.AddWithValue("$completionNote", completion.Session.CompletionNote!);
            command.Parameters.AddWithValue("$nextSequence", completion.Session.NextEventSequence);
            command.Parameters.AddWithValue("$id", completion.Session.Id.ToString("D"));

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The active study session was changed by another operation.");
            }
        }

        await InsertEventAsync(connection, transaction, completion.Event, cancellationToken);

        if (completion.Session.TaskId.HasValue)
        {
            await using var linkedTaskCommand = connection.CreateCommand();
            linkedTaskCommand.Transaction = transaction;
            linkedTaskCommand.CommandText = """
                UPDATE planning_tasks
                SET actual_seconds = actual_seconds + $seconds
                WHERE id = $taskId AND status IN ('active', 'completed');
                """;
            linkedTaskCommand.Parameters.AddWithValue("$seconds", completion.Session.EffectiveDurationSeconds!.Value);
            linkedTaskCommand.Parameters.AddWithValue("$taskId", completion.Session.TaskId.Value.ToString("D"));
            if (await linkedTaskCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The linked planning task no longer exists.");
            }
        }

        if (completion.Session.TimeBlockId.HasValue)
        {
            await using var linkedBlockCommand = connection.CreateCommand();
            linkedBlockCommand.Transaction = transaction;
            linkedBlockCommand.CommandText = """
                UPDATE planning_time_blocks
                SET status = 'completed',
                    actual_seconds = $seconds,
                    resolved_at_utc_ms = $resolvedAt
                WHERE id = $timeBlockId AND status = 'planned';
                """;
            linkedBlockCommand.Parameters.AddWithValue("$seconds", completion.Session.EffectiveDurationSeconds!.Value);
            linkedBlockCommand.Parameters.AddWithValue(
                "$resolvedAt",
                SqliteValueConverters.ToUnixMilliseconds(completion.Session.EndedAtUtc!.Value));
            linkedBlockCommand.Parameters.AddWithValue("$timeBlockId", completion.Session.TimeBlockId.Value.ToString("D"));
            if (await linkedBlockCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The linked time block has already changed.");
            }
        }

        foreach (var task in completion.Tasks)
        {
            await using var taskCommand = connection.CreateCommand();
            taskCommand.Transaction = transaction;
            taskCommand.CommandText = """
                INSERT INTO session_tasks (id, session_id, content, is_completed, sort_order)
                VALUES ($id, $sessionId, $content, $isCompleted, $sortOrder);
                """;
            taskCommand.Parameters.AddWithValue("$id", task.Id.ToString("D"));
            taskCommand.Parameters.AddWithValue("$sessionId", task.SessionId.ToString("D"));
            taskCommand.Parameters.AddWithValue("$content", task.Content);
            taskCommand.Parameters.AddWithValue("$isCompleted", task.IsCompleted ? 1 : 0);
            taskCommand.Parameters.AddWithValue("$sortOrder", task.SortOrder);
            await taskCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await SqliteLedgerWriter.InsertAsync(connection, transaction, completion.Credit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionEvent sessionEvent,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO session_events (id, session_id, sequence, event_type, occurred_at_utc_ms)
            VALUES ($id, $sessionId, $sequence, $eventType, $occurredAt);
            """;
        command.Parameters.AddWithValue("$id", sessionEvent.Id.ToString("D"));
        command.Parameters.AddWithValue("$sessionId", sessionEvent.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", sessionEvent.Sequence);
        command.Parameters.AddWithValue("$eventType", sessionEvent.Type.ToDatabaseValue());
        command.Parameters.AddWithValue("$occurredAt", SqliteValueConverters.ToUnixMilliseconds(sessionEvent.OccurredAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static StudySession ReadSession(SqliteDataReader reader)
    {
        return StudySession.Restore(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            reader.GetBoolean(4),
            SqliteValueConverters.ToStudySessionStatus(reader.GetString(5)),
            SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(6)),
            SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(7)),
            reader.IsDBNull(8) ? null : SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(9)),
            reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetInt32(13));
    }

    private static void AddSessionParameters(SqliteCommand command, StudySession session)
    {
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$goal", session.Goal);
        command.Parameters.AddWithValue("$taskId", session.TaskId.HasValue
            ? session.TaskId.Value.ToString("D")
            : DBNull.Value);
        command.Parameters.AddWithValue("$timeBlockId", session.TimeBlockId.HasValue
            ? session.TimeBlockId.Value.ToString("D")
            : DBNull.Value);
        command.Parameters.AddWithValue("$captureScreenshots", session.CaptureScreenshots ? 1 : 0);
        command.Parameters.AddWithValue("$status", session.Status.ToDatabaseValue());
        command.Parameters.AddWithValue("$startedAt", SqliteValueConverters.ToUnixMilliseconds(session.StartedAtUtc));
        command.Parameters.AddWithValue("$lastTransitionAt", SqliteValueConverters.ToUnixMilliseconds(session.LastTransitionAtUtc));
        command.Parameters.AddWithValue("$pausedSeconds", session.AccumulatedPausedSeconds);
        command.Parameters.AddWithValue("$nextSequence", session.NextEventSequence);
    }
}
