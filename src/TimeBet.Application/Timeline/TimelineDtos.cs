namespace TimeBet.Application.Timeline;

public sealed record TrackingSettings(
    bool CaptureActivity,
    bool CaptureScreenshots,
    int ScreenshotIntervalMinutes,
    int ScreenshotRetentionDays,
    int IdleThresholdMinutes);

public sealed record UpdateTrackingSettingsRequest(
    bool CaptureActivity,
    bool CaptureScreenshots,
    int ScreenshotIntervalMinutes,
    int ScreenshotRetentionDays,
    int IdleThresholdMinutes);

public sealed record TimelineActivityRecord(
    Guid Id,
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string ProcessName,
    string WindowTitle,
    bool IsIdle);

public sealed record TimelineScreenshotRecord(
    Guid Id,
    Guid SessionId,
    DateTimeOffset CapturedAtUtc,
    string FilePath,
    int Width,
    int Height,
    long FileSizeBytes);

public sealed record TimelineSessionRecord(
    Guid Id,
    string Goal,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long EffectiveDurationSeconds,
    IReadOnlyList<TimelineActivityRecord> Activities,
    IReadOnlyList<TimelineScreenshotRecord> Screenshots);

public sealed record TimelineScreenshotSnapshot(
    Guid Id,
    DateTimeOffset CapturedAtUtc,
    string Url,
    int Width,
    int Height);

public sealed record TimelineSessionSnapshot(
    Guid Id,
    string Goal,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long EffectiveDurationSeconds,
    IReadOnlyList<TimelineActivityRecord> Activities,
    IReadOnlyList<TimelineScreenshotSnapshot> Screenshots);

public sealed record StudyTimelineSnapshot(
    DateTimeOffset DayStartedAtUtc,
    DateTimeOffset DayEndedAtUtc,
    long TrackedSeconds,
    long IdleSeconds,
    IReadOnlyList<TimelineSessionSnapshot> Sessions);

public sealed record TrackingOverview(
    TrackingSettings Settings,
    StudyTimelineSnapshot Timeline);
