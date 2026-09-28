namespace TimeBet.Application.Analytics;

public sealed record DailyStudySnapshot(
    string Date,
    DateTimeOffset DayStartedAtUtc,
    long StudySeconds,
    int CompletedSessions);

public sealed record ApplicationUsageSnapshot(
    string ProcessName,
    long DurationSeconds,
    double Share);

public sealed record LedgerTotalsRecord(
    long BalanceSeconds,
    long EarnedSeconds,
    long SpentSeconds);

public sealed record LedgerEntrySnapshot(
    Guid Id,
    string Type,
    long AmountSeconds,
    string SourceType,
    string SourceId,
    string? Note,
    DateTimeOffset OccurredAtUtc);

public sealed record AnalyticsOverviewSnapshot(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset TodayStartedAtUtc,
    DateTimeOffset WeekStartedAtUtc,
    long TodayStudySeconds,
    long WeekStudySeconds,
    int TodayCompletedSessions,
    int WeekCompletedSessions,
    LedgerTotalsRecord Ledger,
    IReadOnlyList<DailyStudySnapshot> DailyTrend,
    IReadOnlyList<ApplicationUsageSnapshot> ApplicationUsage);
