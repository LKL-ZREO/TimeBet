using TimeBet.Domain;
using TimeBet.Domain.Rewards;

namespace TimeBet.Domain.Tests;

public sealed class RewardTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RewardCanBeEditedAndDeactivatedWithoutChangingIdentity()
    {
        var reward = Reward.Create("奶茶", 1800, StartTime, "完成一章后兑换");

        var edited = reward.Edit("电影", 7200, StartTime.AddMinutes(1), "周末观看");
        var inactive = edited.SetActive(false, StartTime.AddMinutes(2));

        Assert.Equal(reward.Id, inactive.Id);
        Assert.Equal("电影", inactive.Name);
        Assert.Equal(7200, inactive.CostSeconds);
        Assert.False(inactive.IsActive);
        Assert.Equal(StartTime, inactive.CreatedAtUtc);
    }

    [Fact]
    public void RewardUpdatesMustBeChronological()
    {
        var reward = Reward.Create("奶茶", 1800, StartTime);

        Assert.Throws<DomainRuleException>(() =>
            reward.Edit("咖啡", 1800, StartTime.AddSeconds(-1)));
    }

    [Fact]
    public void RewardAndRedemptionRejectEmptyIds()
    {
        Assert.Throws<ArgumentException>(() =>
            Reward.Create("奶茶", 1800, StartTime, id: Guid.Empty));
        Assert.Throws<ArgumentException>(() =>
            RewardRedemption.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "奶茶",
                1800,
                StartTime,
                Guid.Empty));
    }
}
