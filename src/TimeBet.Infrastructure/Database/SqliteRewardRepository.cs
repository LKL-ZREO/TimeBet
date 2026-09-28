using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Rewards;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteRewardRepository : IRewardRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteRewardRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Reward>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, description, cost_seconds, image_path, is_active,
                   created_at_utc_ms, updated_at_utc_ms
            FROM rewards
            ORDER BY is_active DESC, updated_at_utc_ms DESC, name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rewards = new List<Reward>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rewards.Add(ReadReward(reader));
        }

        return rewards;
    }

    public async Task<Reward?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, description, cost_seconds, image_path, is_active,
                   created_at_utc_ms, updated_at_utc_ms
            FROM rewards
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReward(reader) : null;
    }

    public async Task AddAsync(Reward reward, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO rewards
                (id, name, description, cost_seconds, image_path, is_active,
                 created_at_utc_ms, updated_at_utc_ms)
            VALUES
                ($id, $name, $description, $costSeconds, $imagePath, $isActive,
                 $createdAt, $updatedAt);
            """;
        AddRewardParameters(command, reward);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(Reward reward, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE rewards
            SET name = $name,
                description = $description,
                cost_seconds = $costSeconds,
                image_path = $imagePath,
                is_active = $isActive,
                updated_at_utc_ms = $updatedAt
            WHERE id = $id;
            """;
        AddRewardParameters(command, reward);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The reward was changed by another operation.");
        }
    }

    public async Task<IReadOnlyList<RewardRedemption>> GetRecentRedemptionsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, reward_id, ledger_entry_id, reward_name_snapshot,
                   cost_seconds, redeemed_at_utc_ms
            FROM reward_redemptions
            ORDER BY redeemed_at_utc_ms DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var redemptions = new List<RewardRedemption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            redemptions.Add(new RewardRedemption(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt64(4),
                FromMilliseconds(reader.GetInt64(5))));
        }

        return redemptions;
    }

    public async Task<RewardRedemptionCommit> RedeemAsync(
        Guid rewardId,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var reward = await GetForRedemptionAsync(connection, transaction, rewardId, cancellationToken)
            ?? throw new DomainRuleException("奖励不存在或已被删除。");
        if (!reward.IsActive)
        {
            throw new DomainRuleException("这个奖励已经停用，无法兑换。");
        }

        var balance = await GetBalanceAsync(connection, transaction, cancellationToken);
        if (balance < reward.CostSeconds)
        {
            throw new DomainRuleException("可用时间不足，继续完成学习后再来兑换。");
        }

        var redemptionId = Guid.NewGuid();
        var ledgerEntry = LedgerEntry.CreateSpend(
            reward.CostSeconds,
            redemptionId.ToString("D"),
            $"兑换奖励：{reward.Name}",
            redeemedAtUtc);
        var redemption = RewardRedemption.Create(
            reward.Id,
            ledgerEntry.Id,
            reward.Name,
            reward.CostSeconds,
            redeemedAtUtc,
            redemptionId);

        await SqliteLedgerWriter.InsertAsync(connection, transaction, ledgerEntry, cancellationToken);
        await InsertRedemptionAsync(connection, transaction, redemption, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RewardRedemptionCommit(redemption, balance - reward.CostSeconds);
    }

    private static async Task<Reward?> GetForRedemptionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid rewardId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, name, description, cost_seconds, image_path, is_active,
                   created_at_utc_ms, updated_at_utc_ms
            FROM rewards
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", rewardId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReward(reader) : null;
    }

    private static async Task<long> GetBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(amount_seconds), 0) FROM ledger_entries;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task InsertRedemptionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RewardRedemption redemption,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO reward_redemptions
                (id, reward_id, ledger_entry_id, reward_name_snapshot,
                 cost_seconds, redeemed_at_utc_ms)
            VALUES
                ($id, $rewardId, $ledgerId, $rewardName, $costSeconds, $redeemedAt);
            """;
        command.Parameters.AddWithValue("$id", redemption.Id.ToString("D"));
        command.Parameters.AddWithValue("$rewardId", redemption.RewardId.ToString("D"));
        command.Parameters.AddWithValue("$ledgerId", redemption.LedgerEntryId.ToString("D"));
        command.Parameters.AddWithValue("$rewardName", redemption.RewardNameSnapshot);
        command.Parameters.AddWithValue("$costSeconds", redemption.CostSeconds);
        command.Parameters.AddWithValue("$redeemedAt", ToMilliseconds(redemption.RedeemedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Reward ReadReward(SqliteDataReader reader)
    {
        return new Reward(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetBoolean(5),
            FromMilliseconds(reader.GetInt64(6)),
            FromMilliseconds(reader.GetInt64(7)));
    }

    private static void AddRewardParameters(SqliteCommand command, Reward reward)
    {
        command.Parameters.AddWithValue("$id", reward.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", reward.Name);
        command.Parameters.AddWithValue("$description", reward.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$costSeconds", reward.CostSeconds);
        command.Parameters.AddWithValue("$imagePath", reward.ImagePath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$isActive", reward.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", ToMilliseconds(reward.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", ToMilliseconds(reward.UpdatedAtUtc));
    }

    private static long ToMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
}
