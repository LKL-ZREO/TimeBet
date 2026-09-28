using System.Globalization;
using TimeBet.Domain;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Rewards;

namespace TimeBet.Infrastructure.Tests;

public sealed class SqliteRewardRepositoryTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RewardCanBeCreatedEditedAndDeactivated()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var reward = Reward.Create("奶茶", 1800, StartTime, "完成高数后");
        await database.Rewards.AddAsync(reward);

        var edited = reward.Edit("咖啡", 2400, StartTime.AddMinutes(1), "完成英语后");
        await database.Rewards.UpdateAsync(edited.SetActive(false, StartTime.AddMinutes(2)));

        var saved = Assert.Single(await database.Rewards.GetAllAsync());
        Assert.Equal("咖啡", saved.Name);
        Assert.Equal(2400, saved.CostSeconds);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task RedeemAtomicallyWritesSnapshotAndNegativeLedgerEntry()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var reward = Reward.Create("电影", 3600, StartTime);
        await database.Rewards.AddAsync(reward);
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(7200, "测试余额", StartTime));

        var commit = await database.Rewards.RedeemAsync(reward.Id, StartTime.AddMinutes(1));

        Assert.Equal(3600, commit.BalanceSeconds);
        Assert.Equal("电影", commit.Redemption.RewardNameSnapshot);
        Assert.Equal(3600, commit.Redemption.CostSeconds);
        Assert.Equal(3600, await database.Ledger.GetBalanceSecondsAsync());
        Assert.Single(await database.Rewards.GetRecentRedemptionsAsync(8));

        await using var connection = await database.Factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT amount_seconds FROM ledger_entries WHERE id = $id;";
        command.Parameters.AddWithValue("$id", commit.Redemption.LedgerEntryId.ToString("D"));
        Assert.Equal(-3600L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task InsufficientBalanceRollsBackWithoutRedemption()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var reward = Reward.Create("新衣服", 7200, StartTime);
        await database.Rewards.AddAsync(reward);
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            database.Rewards.RedeemAsync(reward.Id, StartTime.AddMinutes(1)));

        Assert.Equal(3600, await database.Ledger.GetBalanceSecondsAsync());
        Assert.Empty(await database.Rewards.GetRecentRedemptionsAsync(8));
    }

    [Fact]
    public async Task InactiveRewardCannotBeRedeemed()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var reward = Reward.Create("奶茶", 1800, StartTime)
            .SetActive(false, StartTime.AddSeconds(1));
        await database.Rewards.AddAsync(reward);
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            database.Rewards.RedeemAsync(reward.Id, StartTime.AddMinutes(1)));

        Assert.Equal(3600, await database.Ledger.GetBalanceSecondsAsync());
        Assert.Empty(await database.Rewards.GetRecentRedemptionsAsync(8));
    }

    [Fact]
    public async Task ConcurrentRedeemCanSpendAvailableBalanceOnlyOnce()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var reward = Reward.Create("奶茶", 1800, StartTime);
        await database.Rewards.AddAsync(reward);
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(1800, "测试余额", StartTime));

        var attempts = new[]
        {
            TryRedeemAsync(database, reward.Id),
            TryRedeemAsync(database, reward.Id),
        };
        var outcomes = await Task.WhenAll(attempts);

        Assert.Single(outcomes, outcome => outcome);
        Assert.Equal(0, await database.Ledger.GetBalanceSecondsAsync());
        Assert.Single(await database.Rewards.GetRecentRedemptionsAsync(8));
    }

    private static async Task<bool> TryRedeemAsync(TemporaryDatabase database, Guid rewardId)
    {
        try
        {
            await database.Rewards.RedeemAsync(rewardId, StartTime.AddMinutes(1));
            return true;
        }
        catch (DomainRuleException)
        {
            return false;
        }
    }
}
