using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Challenges;
using TimeBet.Domain;
using TimeBet.Domain.Challenges;
using TimeBet.Domain.Ledger;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteChallengeRepository : IChallengeRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteChallengeRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<ChallengeStateRecord> GetStateAsync(
        DateTimeOffset dayStartedAtUtc,
        DateTimeOffset dayEndedAtUtc,
        int historyCount,
        CancellationToken cancellationToken = default)
    {
        if (historyCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(historyCount));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var enabled = await GetEnabledAsync(connection, null, cancellationToken);
        var balance = await GetBalanceAsync(connection, null, cancellationToken);
        var hasActiveSession = await HasActiveSessionAsync(connection, null, cancellationToken);
        var roundsToday = await CountRoundsAsync(
            connection,
            null,
            dayStartedAtUtc,
            dayEndedAtUtc,
            cancellationToken);
        var recent = await GetRecentRoundsAsync(connection, historyCount, cancellationToken);
        return new ChallengeStateRecord(enabled, balance, hasActiveSession, roundsToday, recent);
    }

    public async Task SetEnabledAsync(
        bool enabled,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE challenge_settings
            SET enabled = $enabled, updated_at_utc_ms = $updatedAt
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToMilliseconds(updatedAtUtc));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("挑战设置不存在。");
        }
    }

    public async Task<ChallengePlayCommit> PlayAsync(
        ChallengePlayCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        var existing = await GetRoundAsync(connection, transaction, command.RoundId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Round.WagerSeconds != command.WagerSeconds)
            {
                throw new DomainRuleException("同一挑战请求不能修改投入时间。");
            }

            var existingBalance = await GetBalanceAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ChallengePlayCommit(existing, existingBalance, true);
        }

        if (!await GetEnabledAsync(connection, transaction, cancellationToken))
        {
            throw new DomainRuleException("请先主动开启星愿机。");
        }

        if (await HasActiveSessionAsync(connection, transaction, cancellationToken))
        {
            throw new DomainRuleException("学习计时进行中不能开启星愿，请先完成当前时间块。");
        }

        var roundsToday = await CountRoundsAsync(
            connection,
            transaction,
            command.DayStartedAtUtc,
            command.DayEndedAtUtc,
            cancellationToken);
        if (roundsToday >= StarWishRules.DailyRoundLimit)
        {
            throw new DomainRuleException("今天的 3 次星愿机会已经用完了。");
        }

        var balance = await GetBalanceAsync(connection, transaction, cancellationToken);
        StarWishRules.ValidateWager(balance, command.WagerSeconds);
        var resolution = StarWishRules.Resolve(command.Outcome, command.WagerSeconds);
        var note = $"星愿机：{resolution.Tier.Label}（投入 {command.WagerSeconds / 60} 分钟，返还 {resolution.PayoutSeconds / 60m:0.#} 分钟）";
        var ledgerEntry = LedgerEntry.ForGameRound(
            resolution.NetSeconds,
            command.RoundId,
            note,
            command.PlayedAtUtc);
        var round = GameRound.Create(
            command.WagerSeconds,
            resolution.PayoutSeconds,
            StarWishRules.ProbabilityVersion,
            command.Outcome,
            ledgerEntry.Id,
            command.PlayedAtUtc,
            command.RoundId);

        await SqliteLedgerWriter.InsertAsync(connection, transaction, ledgerEntry, cancellationToken);
        await InsertRoundAsync(connection, transaction, round, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChallengePlayCommit(
            new ChallengeRoundRecord(round, ledgerEntry.AmountSeconds),
            balance + ledgerEntry.AmountSeconds,
            false);
    }

    private static async Task<bool> GetEnabledAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT enabled FROM challenge_settings WHERE singleton_id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("挑战设置不存在。");
        return Convert.ToInt64(value, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<long> GetBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(amount_seconds), 0) FROM ledger_entries;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasActiveSessionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM study_sessions WHERE active_slot = 1);";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<int> CountRoundsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset dayStartedAtUtc,
        DateTimeOffset dayEndedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM game_rounds
            WHERE played_at_utc_ms >= $dayStartedAt
              AND played_at_utc_ms < $dayEndedAt;
            """;
        command.Parameters.AddWithValue("$dayStartedAt", ToMilliseconds(dayStartedAtUtc));
        command.Parameters.AddWithValue("$dayEndedAt", ToMilliseconds(dayEndedAtUtc));
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<ChallengeRoundRecord>> GetRecentRoundsAsync(
        SqliteConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.wager_seconds, r.payout_seconds, r.probability_version,
                   r.outcome, r.ledger_entry_id, r.played_at_utc_ms, l.amount_seconds
            FROM game_rounds AS r
            INNER JOIN ledger_entries AS l ON l.id = r.ledger_entry_id
            ORDER BY r.played_at_utc_ms DESC, r.id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rounds = new List<ChallengeRoundRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rounds.Add(ReadRound(reader));
        }

        return rounds;
    }

    private static async Task<ChallengeRoundRecord?> GetRoundAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid roundId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.id, r.wager_seconds, r.payout_seconds, r.probability_version,
                   r.outcome, r.ledger_entry_id, r.played_at_utc_ms, l.amount_seconds
            FROM game_rounds AS r
            INNER JOIN ledger_entries AS l ON l.id = r.ledger_entry_id
            WHERE r.id = $id;
            """;
        command.Parameters.AddWithValue("$id", roundId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRound(reader) : null;
    }

    private static ChallengeRoundRecord ReadRound(SqliteDataReader reader)
    {
        var round = new GameRound(
            Guid.Parse(reader.GetString(0)),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetString(3),
            Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture),
            Guid.Parse(reader.GetString(5)),
            FromMilliseconds(reader.GetInt64(6)));
        return new ChallengeRoundRecord(round, reader.GetInt64(7));
    }

    private static async Task InsertRoundAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GameRound round,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO game_rounds
                (id, wager_seconds, payout_seconds, probability_version,
                 outcome, ledger_entry_id, played_at_utc_ms)
            VALUES
                ($id, $wagerSeconds, $payoutSeconds, $probabilityVersion,
                 $outcome, $ledgerEntryId, $playedAt);
            """;
        command.Parameters.AddWithValue("$id", round.Id.ToString("D"));
        command.Parameters.AddWithValue("$wagerSeconds", round.WagerSeconds);
        command.Parameters.AddWithValue("$payoutSeconds", round.PayoutSeconds);
        command.Parameters.AddWithValue("$probabilityVersion", round.ProbabilityVersion);
        command.Parameters.AddWithValue("$outcome", Convert.ToDouble(round.Outcome));
        command.Parameters.AddWithValue("$ledgerEntryId", round.LedgerEntryId.ToString("D"));
        command.Parameters.AddWithValue("$playedAt", ToMilliseconds(round.PlayedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long ToMilliseconds(DateTimeOffset value) =>
        SqliteValueConverters.ToUnixMilliseconds(value);

    private static DateTimeOffset FromMilliseconds(long value) =>
        SqliteValueConverters.FromUnixMilliseconds(value);
}
