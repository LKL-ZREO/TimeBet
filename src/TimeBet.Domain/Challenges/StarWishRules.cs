namespace TimeBet.Domain.Challenges;

public static class StarWishRules
{
    public const string ProbabilityVersion = "star-wish-v1";
    public const long MinimumBalanceSeconds = 10 * 60;
    public const long MinimumWagerSeconds = 60;
    public const long MaximumWagerSeconds = 30 * 60;
    public const int DailyRoundLimit = 3;
    public const decimal MaximumWagerBalanceShare = 0.10m;

    private static readonly IReadOnlyList<StarWishTier> tiers = Array.AsReadOnly<StarWishTier>(
    [
        new("starlight-fades", "星光熄灭", 0.40m, 0m, ["moon", "cloud", "book"]),
        new("ember-remains", "留下火种", 0.20m, 0.5m, ["spark", "book", "spark"]),
        new("small-fortune", "小小幸运", 0.25m, 1.5m, ["book", "star", "book"]),
        new("double-progress", "双倍进展", 0.12m, 3m, ["star", "medal", "star"]),
        new("wish-arrives", "星愿降临", 0.03m, 4m, ["crown", "crown", "crown"]),
    ]);

    public static IReadOnlyList<StarWishTier> Tiers => tiers;

    public static decimal ExpectedReturn =>
        tiers.Sum(tier => tier.Probability * tier.PayoutMultiplier);

    public static StarWishResolution Resolve(decimal outcome, long wagerSeconds)
    {
        if (outcome is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "Outcome must be in the [0, 1) interval.");
        }

        if (wagerSeconds <= 0 || wagerSeconds % MinimumWagerSeconds != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wagerSeconds), "Wager must use whole-minute steps.");
        }

        var upperBound = 0m;
        foreach (var tier in tiers)
        {
            upperBound += tier.Probability;
            if (outcome < upperBound)
            {
                var payoutSeconds = checked((long)(wagerSeconds * tier.PayoutMultiplier));
                return new StarWishResolution(tier, payoutSeconds, payoutSeconds - wagerSeconds);
            }
        }

        throw new InvalidOperationException("Star Wish probabilities do not cover the full unit interval.");
    }

    public static long GetMaximumWagerSeconds(long balanceSeconds)
    {
        if (balanceSeconds < MinimumBalanceSeconds)
        {
            return 0;
        }

        var percentageLimit = (long)(balanceSeconds * MaximumWagerBalanceShare);
        var roundedToMinute = percentageLimit / MinimumWagerSeconds * MinimumWagerSeconds;
        return Math.Min(MaximumWagerSeconds, roundedToMinute);
    }

    public static void ValidateWager(long balanceSeconds, long wagerSeconds)
    {
        if (balanceSeconds < MinimumBalanceSeconds)
        {
            throw new DomainRuleException("至少积累 10 分钟可用时间后才能开启星愿。");
        }

        if (wagerSeconds < MinimumWagerSeconds || wagerSeconds % MinimumWagerSeconds != 0)
        {
            throw new DomainRuleException("投入时间必须是至少 1 分钟的整分钟数。");
        }

        var maximum = GetMaximumWagerSeconds(balanceSeconds);
        if (wagerSeconds > maximum)
        {
            throw new DomainRuleException($"本次最多可以投入 {maximum / 60} 分钟。");
        }
    }
}

public sealed record StarWishTier(
    string Code,
    string Label,
    decimal Probability,
    decimal PayoutMultiplier,
    IReadOnlyList<string> Symbols);

public sealed record StarWishResolution(
    StarWishTier Tier,
    long PayoutSeconds,
    long NetSeconds);
