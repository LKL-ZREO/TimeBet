namespace TimeBet.Application.Reviews;

public sealed record DailyReviewMetrics(
    long PlannedSeconds,
    long StudySeconds,
    long TrackedSeconds,
    long IdleSeconds,
    int PlannedBlockCount,
    int CompletedBlockCount,
    int SkippedBlockCount,
    int CompletedSessionCount);

public sealed record DailyReviewRelevance(
    bool ActivityTrackingEnabled,
    long EvaluatedSeconds,
    long HighSeconds,
    long PossibleSeconds,
    long LowSeconds,
    long UnknownSeconds);

public sealed record DailyReviewApplication(
    string ProcessName,
    long DurationSeconds,
    double Share);

public sealed record DailyReviewSession(
    Guid Id,
    string Goal,
    string Status,
    long DurationSeconds,
    long IdleSeconds,
    long HighSeconds,
    long PossibleSeconds,
    long LowSeconds,
    long UnknownSeconds,
    IReadOnlyList<DailyReviewApplication> Applications);

public sealed record DailyReviewSnapshot(
    string Date,
    DateTimeOffset GeneratedAtUtc,
    bool HasData,
    string Source,
    string? AiModel,
    string Headline,
    string Narrative,
    DailyReviewMetrics Metrics,
    DailyReviewRelevance Relevance,
    IReadOnlyList<DailyReviewApplication> Applications,
    IReadOnlyList<DailyReviewSession> Sessions,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Suggestions);
