namespace TimeBet.Application.Sessions;

public sealed record SessionTaskInput(string Content, bool IsCompleted);

public sealed record StartSessionRequest(
    string? Goal,
    bool CaptureScreenshots = false,
    Guid? TaskId = null,
    Guid? TimeBlockId = null);

public sealed record FinishSessionRequest(
    string? CompletionNote,
    IReadOnlyList<SessionTaskInput>? Tasks);

public sealed record StudySessionSnapshot(
    Guid Id,
    string Goal,
    Guid? TaskId,
    Guid? TimeBlockId,
    bool CaptureScreenshots,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastTransitionAtUtc,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long EffectiveDurationSeconds,
    string? CompletionNote);

public sealed record StudyStateSnapshot(
    StudySessionSnapshot? Session,
    long BalanceSeconds,
    DateTimeOffset AsOfUtc);

public sealed record FinishSessionResult(
    StudyStateSnapshot State,
    StudySessionSnapshot CompletedSession);
