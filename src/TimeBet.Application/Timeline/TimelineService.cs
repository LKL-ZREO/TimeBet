using TimeBet.Application.Abstractions;

namespace TimeBet.Application.Timeline;

public sealed class TimelineService
{
    private const string ScreenshotHost = "screenshots.timebet.local";
    private static readonly int[] AllowedIntervals = [1, 5, 10, 15, 30];
    private static readonly int[] AllowedRetentionDays = [7, 30, 90, 3650];
    private readonly IActivityTimelineRepository timeline;
    private readonly ITrackingSettingsRepository settings;
    private readonly string screenshotRoot;

    public TimelineService(
        IActivityTimelineRepository timeline,
        ITrackingSettingsRepository settings,
        string screenshotRoot)
    {
        this.timeline = timeline;
        this.settings = settings;
        this.screenshotRoot = Path.GetFullPath(screenshotRoot);
    }

    public async Task<TrackingOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var currentSettings = await settings.GetAsync(cancellationToken);
        var now = DateTimeOffset.Now;
        var localStart = new DateTimeOffset(now.Date, now.Offset);
        var localEnd = localStart.AddDays(1);
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();
        var sessions = await timeline.GetSessionsAsync(endUtc, startUtc, cancellationToken);
        var snapshots = sessions.Select(ToSnapshot).ToArray();

        return new TrackingOverview(
            currentSettings,
            new StudyTimelineSnapshot(
                startUtc,
                endUtc,
                snapshots.Sum(session => session.Activities.Sum(ActivityDurationSeconds)),
                snapshots.Sum(session => session.Activities.Where(activity => activity.IsIdle).Sum(ActivityDurationSeconds)),
                snapshots));
    }

    public Task<TrackingSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return settings.GetAsync(cancellationToken);
    }

    public async Task<TrackingSettings> UpdateSettingsAsync(
        UpdateTrackingSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedIntervals.Contains(request.ScreenshotIntervalMinutes))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "截图间隔不受支持。");
        }

        if (!AllowedRetentionDays.Contains(request.ScreenshotRetentionDays))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "截图保留期不受支持。");
        }

        if (request.IdleThresholdMinutes is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "空闲阈值必须在 1 到 60 分钟之间。");
        }

        if (request.CaptureScreenshots && !request.CaptureActivity)
        {
            throw new ArgumentException("开启截图前必须先开启活动记录。", nameof(request));
        }

        var updated = new TrackingSettings(
            request.CaptureActivity,
            request.CaptureScreenshots,
            request.ScreenshotIntervalMinutes,
            request.ScreenshotRetentionDays,
            request.IdleThresholdMinutes);
        await settings.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private TimelineSessionSnapshot ToSnapshot(TimelineSessionRecord session)
    {
        return new TimelineSessionSnapshot(
            session.Id,
            session.Goal,
            session.Status,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.EffectiveDurationSeconds,
            session.Activities,
            session.Screenshots
                .Select(screenshot => new TimelineScreenshotSnapshot(
                    screenshot.Id,
                    screenshot.CapturedAtUtc,
                    ToScreenshotUrl(screenshot.FilePath),
                    screenshot.Width,
                    screenshot.Height))
                .ToArray());
    }

    private string ToScreenshotUrl(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var relative = Path.GetRelativePath(screenshotRoot, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("截图路径超出本地截图目录。");
        }

        var escaped = string.Join('/', relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Select(Uri.EscapeDataString));
        return $"https://{ScreenshotHost}/{escaped}";
    }

    private static long ActivityDurationSeconds(TimelineActivityRecord activity)
    {
        return Math.Max(0, (long)Math.Floor((activity.EndedAtUtc - activity.StartedAtUtc).TotalSeconds));
    }
}
