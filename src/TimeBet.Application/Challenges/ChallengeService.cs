using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Challenges;

namespace TimeBet.Application.Challenges;

public sealed class ChallengeService
{
    private const int HistoryCount = 8;
    private readonly IChallengeRepository challenges;
    private readonly IClock clock;
    private readonly IRandomSource random;
    private readonly TimeZoneInfo timeZone;

    public ChallengeService(
        IChallengeRepository challenges,
        IClock clock,
        IRandomSource random,
        TimeZoneInfo? timeZone = null)
    {
        this.challenges = challenges;
        this.clock = clock;
        this.random = random;
        this.timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public async Task<ChallengeOverviewSnapshot> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow.ToUniversalTime();
        var (dayStartUtc, dayEndUtc) = GetLocalDayRange(nowUtc);
        var state = await challenges.GetStateAsync(
            dayStartUtc,
            dayEndUtc,
            HistoryCount,
            cancellationToken);
        return ToOverview(state);
    }

    public async Task<ChallengeOverviewSnapshot> UpdateSettingsAsync(
        UpdateChallengeSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await challenges.SetEnabledAsync(request.Enabled, clock.UtcNow, cancellationToken);
        return await GetOverviewAsync(cancellationToken);
    }

    public async Task<PlayChallengeResult> PlayAsync(
        PlayChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RoundId == Guid.Empty)
        {
            throw new ArgumentException("挑战局 ID 无效。", nameof(request));
        }

        if (request.WagerSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "请输入有效的投入时间。");
        }

        var nowUtc = clock.UtcNow.ToUniversalTime();
        var (dayStartUtc, dayEndUtc) = GetLocalDayRange(nowUtc);
        var outcome = Convert.ToDecimal(random.NextUnitInterval());
        _ = StarWishRules.Resolve(outcome, request.WagerSeconds);
        var commit = await challenges.PlayAsync(
            new ChallengePlayCommand(
                request.RoundId,
                request.WagerSeconds,
                outcome,
                nowUtc,
                dayStartUtc,
                dayEndUtc),
            cancellationToken);
        var state = await challenges.GetStateAsync(
            dayStartUtc,
            dayEndUtc,
            HistoryCount,
            cancellationToken);
        var round = ToSnapshot(commit.Round);
        return new PlayChallengeResult(ToOverview(state), round);
    }

    private static ChallengeOverviewSnapshot ToOverview(ChallengeStateRecord state)
    {
        return new ChallengeOverviewSnapshot(
            state.Enabled,
            state.BalanceSeconds,
            state.HasActiveStudySession,
            state.RoundsPlayedToday,
            Math.Max(0, StarWishRules.DailyRoundLimit - state.RoundsPlayedToday),
            StarWishRules.MinimumBalanceSeconds,
            StarWishRules.MinimumWagerSeconds,
            StarWishRules.GetMaximumWagerSeconds(state.BalanceSeconds),
            StarWishRules.DailyRoundLimit,
            StarWishRules.MaximumWagerBalanceShare,
            StarWishRules.ExpectedReturn,
            StarWishRules.ProbabilityVersion,
            StarWishRules.Tiers.Select(tier => new ChallengeProbabilityTierSnapshot(
                tier.Code,
                tier.Label,
                tier.Probability,
                tier.PayoutMultiplier,
                tier.Symbols)).ToArray(),
            state.RecentRounds.Select(ToSnapshot).ToArray());
    }

    private static ChallengeRoundSnapshot ToSnapshot(ChallengeRoundRecord record)
    {
        var resolution = StarWishRules.Resolve(record.Round.Outcome, record.Round.WagerSeconds);
        if (!string.Equals(
                record.Round.ProbabilityVersion,
                StarWishRules.ProbabilityVersion,
                StringComparison.Ordinal))
        {
            throw new DomainRuleException($"不支持的概率版本：{record.Round.ProbabilityVersion}");
        }

        return new ChallengeRoundSnapshot(
            record.Round.Id,
            record.Round.WagerSeconds,
            record.Round.PayoutSeconds,
            record.NetSeconds,
            resolution.Tier.Code,
            resolution.Tier.Label,
            resolution.Tier.Symbols,
            record.Round.PlayedAtUtc);
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetLocalDayRange(DateTimeOffset nowUtc)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).Date);
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
    }
}
