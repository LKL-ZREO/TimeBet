namespace TimeBet.Application.Leisure;

public sealed record StartLeisureRequest(string? Activity, int PlannedMinutes);

public sealed record FinishLeisureRequest(string? CompletionNote);

public sealed record LeisureSessionSnapshot(
    Guid Id,
    string Activity,
    int PlannedSeconds,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastTransitionAtUtc,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long EffectiveDurationSeconds,
    long ChargedSeconds,
    string? CompletionNote);

public sealed record LeisureStateSnapshot(
    LeisureSessionSnapshot? Session,
    long BalanceSeconds,
    DateTimeOffset AsOfUtc);

public sealed record FinishLeisureResult(
    LeisureStateSnapshot State,
    LeisureSessionSnapshot CompletedSession);
