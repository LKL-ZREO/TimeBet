using TimeBet.Domain;
using TimeBet.Domain.Challenges;
using TimeBet.Domain.Ledger;

namespace TimeBet.Domain.Tests;

public sealed class StarWishRulesTests
{
    [Theory]
    [InlineData(0.00, "starlight-fades", 0, -600)]
    [InlineData(0.40, "ember-remains", 300, -300)]
    [InlineData(0.60, "small-fortune", 900, 300)]
    [InlineData(0.85, "double-progress", 1800, 1200)]
    [InlineData(0.97, "wish-arrives", 2400, 1800)]
    public void ProbabilityBoundariesResolveToExpectedTier(
        double outcome,
        string code,
        long payoutSeconds,
        long netSeconds)
    {
        var result = StarWishRules.Resolve(Convert.ToDecimal(outcome), 600);

        Assert.Equal(code, result.Tier.Code);
        Assert.Equal(payoutSeconds, result.PayoutSeconds);
        Assert.Equal(netSeconds, result.NetSeconds);
    }

    [Fact]
    public void ProbabilityTableHasExpectedReturnAndCoversUnitInterval()
    {
        Assert.Equal(1m, StarWishRules.Tiers.Sum(tier => tier.Probability));
        Assert.Equal(
            0.955m,
            StarWishRules.Tiers.Sum(tier => tier.Probability * tier.PayoutMultiplier));
    }

    [Theory]
    [InlineData(599, 0)]
    [InlineData(600, 60)]
    [InlineData(3600, 360)]
    [InlineData(18000, 1800)]
    [InlineData(36000, 1800)]
    public void MaximumWagerUsesTenPercentRoundedToMinutesAndAbsoluteCap(
        long balanceSeconds,
        long expected)
    {
        Assert.Equal(expected, StarWishRules.GetMaximumWagerSeconds(balanceSeconds));
    }

    [Fact]
    public void WagerValidationRequiresMinimumBalanceAndWholeMinuteSteps()
    {
        Assert.Throws<DomainRuleException>(() => StarWishRules.ValidateWager(599, 60));
        Assert.Throws<DomainRuleException>(() => StarWishRules.ValidateWager(3600, 61));
        Assert.Throws<DomainRuleException>(() => StarWishRules.ValidateWager(3600, 420));
        StarWishRules.ValidateWager(3600, 360);
    }

    [Fact]
    public void GameLedgerEntriesUseNetResultAndImmutableRoundSource()
    {
        var roundId = Guid.NewGuid();
        var win = LedgerEntry.ForGameRound(300, roundId, "小小幸运", DateTimeOffset.UtcNow);
        var loss = LedgerEntry.ForGameRound(-300, Guid.NewGuid(), "留下火种", DateTimeOffset.UtcNow);

        Assert.Equal(LedgerEntryType.GameWin, win.Type);
        Assert.Equal("game-round", win.SourceType);
        Assert.Equal(roundId.ToString("D"), win.SourceId);
        Assert.Equal(LedgerEntryType.GameLoss, loss.Type);
    }
}
