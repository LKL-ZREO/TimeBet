using TimeBet.Application.Abstractions;
using TimeBet.Application.Timeline;
using TimeBet.Domain.Sessions;
using TimeBet.Domain.Timeline;

namespace TimeBet.Tracker.Windows;

public sealed class StudyActivityTracker : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private readonly IStudySessionRepository sessions;
    private readonly IActivityTimelineRepository timeline;
    private readonly ITrackingSettingsRepository settingsRepository;
    private readonly IClock clock;
    private readonly IWindowsActivitySource activitySource;
    private readonly IWindowsScreenshotCapture screenshotCapture;
    private readonly ActivityEvidenceBuffer? activityEvidence;
    private readonly string screenshotRoot;
    private readonly CancellationTokenSource shutdown = new();
    private Task? runTask;
    private DateTimeOffset? lastScreenshotAtUtc;
    private DateTimeOffset? lastCleanupAtUtc;

    public StudyActivityTracker(
        IStudySessionRepository sessions,
        IActivityTimelineRepository timeline,
        ITrackingSettingsRepository settingsRepository,
        IClock clock,
        IWindowsActivitySource activitySource,
        IWindowsScreenshotCapture screenshotCapture,
        string screenshotRoot,
        ActivityEvidenceBuffer? activityEvidence = null)
    {
        this.sessions = sessions;
        this.timeline = timeline;
        this.settingsRepository = settingsRepository;
        this.clock = clock;
        this.activitySource = activitySource;
        this.screenshotCapture = screenshotCapture;
        this.activityEvidence = activityEvidence;
        this.screenshotRoot = Path.GetFullPath(screenshotRoot);
    }

    public void Start()
    {
        runTask ??= RunAsync(shutdown.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync();
        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await CaptureOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Tracking must never terminate the study timer or desktop process.
            }
        }
    }

    internal async Task CaptureOnceAsync(CancellationToken cancellationToken = default)
    {
        var activeSession = await sessions.GetActiveAsync(cancellationToken);
        if (activeSession?.Status != StudySessionStatus.Running)
        {
            lastScreenshotAtUtc = null;
            return;
        }

        var settings = await settingsRepository.GetAsync(cancellationToken);
        if (!settings.CaptureActivity)
        {
            lastScreenshotAtUtc = null;
            return;
        }

        var window = activitySource.GetCurrent();
        if (window is null)
        {
            return;
        }

        var now = clock.UtcNow;
        activityEvidence?.Observe(window, now);
        var sampleStart = now - SampleInterval;
        if (sampleStart < activeSession.LastTransitionAtUtc)
        {
            sampleStart = activeSession.LastTransitionAtUtc;
        }

        if (sampleStart >= now)
        {
            return;
        }
        var idle = window.IdleDuration >= TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
        var segment = ActivitySegment.Create(
            activeSession.Id,
            sampleStart,
            now,
            window.ProcessName,
            window.WindowTitle,
            idle);
        await timeline.AppendOrMergeActivityAsync(segment, cancellationToken);

        if (settings.CaptureScreenshots &&
            activeSession.CaptureScreenshots &&
            !idle &&
            ShouldCaptureScreenshot(
                now,
                activeSession.StartedAtUtc,
                settings.ScreenshotIntervalMinutes))
        {
            var filePath = BuildScreenshotPath(activeSession.Id, now);
            var captured = screenshotCapture.Capture(window, filePath);
            if (captured is not null)
            {
                var record = ScreenshotRecord.Create(
                    activeSession.Id,
                    now,
                    captured.FilePath,
                    captured.Width,
                    captured.Height,
                    captured.FileSizeBytes);
                try
                {
                    await timeline.AddScreenshotAsync(record, cancellationToken);
                }
                catch
                {
                    File.Delete(captured.FilePath);
                    throw;
                }

                lastScreenshotAtUtc = now;
            }
        }

        if (!lastCleanupAtUtc.HasValue || now - lastCleanupAtUtc.Value >= TimeSpan.FromHours(6))
        {
            await DeleteExpiredScreenshotsAsync(now, settings.ScreenshotRetentionDays, cancellationToken);
            lastCleanupAtUtc = now;
        }
    }

    private bool ShouldCaptureScreenshot(
        DateTimeOffset now,
        DateTimeOffset sessionStartedAtUtc,
        int intervalMinutes)
    {
        var previous = lastScreenshotAtUtc ?? sessionStartedAtUtc;
        return now - previous >= TimeSpan.FromMinutes(intervalMinutes);
    }

    private string BuildScreenshotPath(Guid sessionId, DateTimeOffset capturedAtUtc)
    {
        var fileName = $"{capturedAtUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jpg";
        return Path.Combine(screenshotRoot, sessionId.ToString("N"), fileName);
    }

    private async Task DeleteExpiredScreenshotsAsync(
        DateTimeOffset now,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-retentionDays);
        var paths = await timeline.DeleteScreenshotsBeforeAsync(cutoff, cancellationToken);
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var relative = Path.GetRelativePath(screenshotRoot, fullPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                {
                    File.Delete(fullPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            if (Directory.Exists(screenshotRoot))
            {
                foreach (var file in Directory.EnumerateFiles(screenshotRoot, "*.jpg", SearchOption.AllDirectories))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
