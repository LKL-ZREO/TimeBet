namespace TimeBet.Application.Tasks;

public sealed record CreateTimeBlockRequest(
    Guid TaskId,
    DateTimeOffset StartsAtUtc,
    int PlannedMinutes);

public sealed record SkipTimeBlockRequest(Guid Id);

public sealed record MoveTimeBlockRequest(
    Guid Id,
    DateTimeOffset StartsAtUtc,
    int PlannedMinutes);

public sealed record DeferTimeBlockRequest(Guid Id);

public sealed record PlanningTimeBlockSnapshot(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    DateOnly? DueDate,
    DateTimeOffset StartsAtUtc,
    int PlannedSeconds,
    long ActualSeconds,
    string Status,
    DateTimeOffset? ResolvedAtUtc);

public sealed record TodayPlanSnapshot(
    string Date,
    DateTimeOffset DayStartedAtUtc,
    DateTimeOffset DayEndedAtUtc,
    long PlannedSeconds,
    long ActualSeconds,
    int PlannedCount,
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<PlanningTimeBlockSnapshot> Blocks);

public sealed record PlanDaySnapshot(
    string Date,
    DateTimeOffset DayStartedAtUtc,
    DateTimeOffset DayEndedAtUtc,
    long PlannedSeconds,
    long ActualSeconds,
    int PlannedCount,
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<PlanningTimeBlockSnapshot> Blocks);

public sealed record WeekPlanSnapshot(
    string WeekStartedDate,
    DateTimeOffset WeekStartedAtUtc,
    DateTimeOffset WeekEndedAtUtc,
    long PlannedSeconds,
    long ActualSeconds,
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<PlanDaySnapshot> Days);
