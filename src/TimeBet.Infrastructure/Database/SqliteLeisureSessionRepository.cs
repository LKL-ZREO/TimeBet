using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Leisure;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteLeisureSessionRepository : ILeisureSessionRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteLeisureSessionRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AddAsync(
        LeisureSession session,
        CancellationToken cancellationToken = default)
    {
        if (session.Status != LeisureSessionStatus.Running)
        {
            throw new ArgumentException("A new leisure session must be running.", nameof(session));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO leisure_sessions
                (id, activity, planned_seconds, status, started_at_utc_ms, last_transition_at_utc_ms,
                 paused_at_utc_ms, ended_at_utc_ms, accumulated_paused_seconds,
                 effective_duration_seconds, charged_seconds, completion_note, active_slot)
            VALUES
                ($id, $activity, $plannedSeconds, 'running', $startedAt, $lastTransitionAt,
                 NULL, NULL, 0, NULL, 0, NULL, 1);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$activity", session.Activity);
        command.Parameters.AddWithValue("$plannedSeconds", session.PlannedSeconds);
        command.Parameters.AddWithValue("$startedAt", ToMilliseconds(session.StartedAtUtc));
        command.Parameters.AddWithValue("$lastTransitionAt", ToMilliseconds(session.LastTransitionAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LeisureSession?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE active_slot = 1 LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task SaveAsync(
        LeisureSession session,
        CancellationToken cancellationToken = default)
    {
        if (session.Status is not (LeisureSessionStatus.Running or LeisureSessionStatus.Paused))
        {
            throw new ArgumentException("Only active leisure sessions can be updated.", nameof(session));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE leisure_sessions
            SET status = $status,
                last_transition_at_utc_ms = $lastTransitionAt,
                paused_at_utc_ms = $pausedAt,
                accumulated_paused_seconds = $pausedSeconds
            WHERE id = $id AND active_slot = 1 AND status IN ('running', 'paused');
            """;
        command.Parameters.AddWithValue("$status", ToDatabaseValue(session.Status));
        command.Parameters.AddWithValue("$lastTransitionAt", ToMilliseconds(session.LastTransitionAtUtc));
        command.Parameters.AddWithValue("$pausedAt", session.PausedAtUtc.HasValue
            ? ToMilliseconds(session.PausedAtUtc.Value)
            : DBNull.Value);
        command.Parameters.AddWithValue("$pausedSeconds", session.AccumulatedPausedSeconds);
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The active leisure session was changed by another operation.");
        }
    }

    public async Task<LeisureSettlementResult> CompleteAndDebitAsync(
        LeisureSession completed,
        CancellationToken cancellationToken = default)
    {
        if (completed.Status != LeisureSessionStatus.Completed || !completed.EffectiveDurationSeconds.HasValue)
        {
            throw new ArgumentException("The leisure session must be completed before settlement.", nameof(completed));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        long balance;
        await using (var balanceCommand = connection.CreateCommand())
        {
            balanceCommand.Transaction = transaction;
            balanceCommand.CommandText = "SELECT COALESCE(SUM(amount_seconds), 0) FROM ledger_entries;";
            balance = Convert.ToInt64(
                await balanceCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        var chargedSeconds = Math.Min(completed.EffectiveDurationSeconds.Value, Math.Max(0, balance));
        var settled = completed.SetChargedSeconds(chargedSeconds);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE leisure_sessions
                SET status = 'completed',
                    last_transition_at_utc_ms = $lastTransitionAt,
                    paused_at_utc_ms = NULL,
                    ended_at_utc_ms = $endedAt,
                    accumulated_paused_seconds = $pausedSeconds,
                    effective_duration_seconds = $effectiveSeconds,
                    charged_seconds = $chargedSeconds,
                    completion_note = $completionNote,
                    active_slot = NULL
                WHERE id = $id AND active_slot = 1 AND status IN ('running', 'paused');
                """;
            update.Parameters.AddWithValue("$lastTransitionAt", ToMilliseconds(settled.LastTransitionAtUtc));
            update.Parameters.AddWithValue("$endedAt", ToMilliseconds(settled.EndedAtUtc!.Value));
            update.Parameters.AddWithValue("$pausedSeconds", settled.AccumulatedPausedSeconds);
            update.Parameters.AddWithValue("$effectiveSeconds", settled.EffectiveDurationSeconds!.Value);
            update.Parameters.AddWithValue("$chargedSeconds", settled.ChargedSeconds);
            update.Parameters.AddWithValue("$completionNote", (object?)settled.CompletionNote ?? DBNull.Value);
            update.Parameters.AddWithValue("$id", settled.Id.ToString("D"));
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The active leisure session was changed by another operation.");
            }
        }

        if (chargedSeconds > 0)
        {
            await SqliteLedgerWriter.InsertAsync(
                connection,
                transaction,
                LedgerEntry.ForLeisureSession(settled, settled.EndedAtUtc!.Value),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new LeisureSettlementResult(settled, balance - chargedSeconds);
    }

    private const string BaseSelect = """
        SELECT id, activity, planned_seconds, status, started_at_utc_ms, last_transition_at_utc_ms,
               paused_at_utc_ms, ended_at_utc_ms, accumulated_paused_seconds,
               effective_duration_seconds, charged_seconds, completion_note
        FROM leisure_sessions
        """;

    private static LeisureSession Read(SqliteDataReader reader)
    {
        return LeisureSession.Restore(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            ToStatus(reader.GetString(3)),
            FromMilliseconds(reader.GetInt64(4)),
            FromMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : FromMilliseconds(reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : FromMilliseconds(reader.GetInt64(7)),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private static string ToDatabaseValue(LeisureSessionStatus status) => status switch
    {
        LeisureSessionStatus.Running => "running",
        LeisureSessionStatus.Paused => "paused",
        LeisureSessionStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static LeisureSessionStatus ToStatus(string value) => value switch
    {
        "running" => LeisureSessionStatus.Running,
        "paused" => LeisureSessionStatus.Paused,
        "completed" => LeisureSessionStatus.Completed,
        _ => throw new InvalidOperationException($"Unknown leisure session status '{value}'."),
    };

    private static long ToMilliseconds(DateTimeOffset value) =>
        SqliteValueConverters.ToUnixMilliseconds(value);

    private static DateTimeOffset FromMilliseconds(long value) =>
        SqliteValueConverters.FromUnixMilliseconds(value);
}
