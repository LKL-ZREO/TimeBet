namespace TimeBet.Application.Challenges;

public sealed record UpdateChallengeSettingsRequest(bool Enabled);

public sealed record PlayChallengeRequest(Guid RoundId, long WagerSeconds);

public sealed record ChallengeProbabilityTierSnapshot(
    string Code,
    string Label,
    decimal Probability,
    decimal PayoutMultiplier,
    IReadOnlyList<string> Symbols);

public sealed record ChallengeRoundSnapshot(
    Guid Id,
    long WagerSeconds,
    long PayoutSeconds,
    long NetSeconds,
    string OutcomeCode,
    string OutcomeLabel,
    IReadOnlyList<string> Symbols,
    DateTimeOffset PlayedAtUtc);

public sealed record ChallengeOverviewSnapshot(
    bool Enabled,
    long BalanceSeconds,
    bool HasActiveStudySession,
    int RoundsPlayedToday,
    int RoundsRemainingToday,
    long MinimumBalanceSeconds,
    long MinimumWagerSeconds,
    long MaximumWagerSeconds,
    int DailyRoundLimit,
    decimal MaximumWagerBalanceShare,
    decimal ExpectedReturn,
    string ProbabilityVersion,
    IReadOnlyList<ChallengeProbabilityTierSnapshot> ProbabilityTiers,
    IReadOnlyList<ChallengeRoundSnapshot> RecentRounds);

public sealed record PlayChallengeResult(
    ChallengeOverviewSnapshot Overview,
    ChallengeRoundSnapshot Round);
