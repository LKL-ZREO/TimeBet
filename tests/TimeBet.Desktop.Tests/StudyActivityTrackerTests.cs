using TimeBet.Application.Abstractions;
using TimeBet.Application.Sessions;
using TimeBet.Application.Timeline;
using TimeBet.Domain.Sessions;
using TimeBet.Domain.Timeline;
using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop.Tests;

public sealed class StudyActivityTrackerTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunningSessionCapturesActivityButScreenshotsDefaultOff()
    {
        var context = CreateContext(StudySessionStatus.Running);
        await using var tracker = context.Tracker;

        await tracker.CaptureOnceAsync();

        Assert.Single(context.Timeline.Activities);
        Assert.Empty(context.Timeline.Screenshots);
        Assert.Equal("Code", context.Timeline.Activities[0].ProcessName);
        Assert.False(context.Timeline.Activities[0].IsIdle);
    }

    [Fact]
    public async Task PausedSessionDoesNotCaptureAnything()
    {
        var context = CreateContext(StudySessionStatus.Paused);
        await using var tracker = context.Tracker;

        await tracker.CaptureOnceAsync();

        Assert.Empty(context.Timeline.Activities);
        Assert.Empty(context.Timeline.Screenshots);
    }

    [Fact]
    public async Task IdleSampleIsRecordedButNeverScreenshotted()
    {
        var context = CreateContext(
            StudySessionStatus.Running,
            new TrackingSettings(true, true, 1, 7, 5),
            TimeSpan.FromMinutes(6));
        await using var tracker = context.Tracker;

        await tracker.CaptureOnceAsync();

        Assert.True(Assert.Single(context.Timeline.Activities).IsIdle);
        Assert.Empty(context.Timeline.Screenshots);
        Assert.Equal(0, context.ScreenshotCapture.CaptureCount);
    }

    [Fact]
    public async Task GlobalScreenshotSettingStillRequiresSessionConsent()
    {
        var context = CreateContext(
            StudySessionStatus.Running,
            new TrackingSettings(true, true, 1, 7, 5),
            TimeSpan.Zero);
        await using var tracker = context.Tracker;

        await tracker.CaptureOnceAsync();

        Assert.Empty(context.Timeline.Screenshots);
        Assert.Equal(0, context.ScreenshotCapture.CaptureCount);
    }

    [Fact]
    public async Task ScreenshotEnabledCapturesNonIdleForegroundWindow()
    {
        var context = CreateContext(
            StudySessionStatus.Running,
            new TrackingSettings(true, true, 1, 7, 5),
            TimeSpan.Zero,
            allowScreenshots: true);
        await using var tracker = context.Tracker;

        await tracker.CaptureOnceAsync();

        Assert.Single(context.Timeline.Screenshots);
        Assert.Equal(1, context.ScreenshotCapture.CaptureCount);
    }

    private static TestContext CreateContext(
        StudySessionStatus status,
        TrackingSettings? settings = null,
        TimeSpan? idleDuration = null,
        bool allowScreenshots = false)
    {
        var transition = StudySession.Start("测试学习", StartTime, allowScreenshots);
        var active = status == StudySessionStatus.Paused
            ? transition.Session.Pause(StartTime.AddSeconds(1)).Session
            : transition.Session;
        var clock = new TestClock { UtcNow = StartTime.AddSeconds(65) };
        var timeline = new MemoryTimeline();
        var screenshots = new FakeScreenshotCapture();
        var tracker = new StudyActivityTracker(
            new SessionRepository(active),
            timeline,
            new SettingsRepository(settings ?? new TrackingSettings(true, false, 5, 7, 5)),
            clock,
            new ActivitySource(idleDuration ?? TimeSpan.Zero),
            screenshots,
            Path.Combine(Path.GetTempPath(), "TimeBet.Tracker.Tests"));
        return new TestContext(tracker, timeline, screenshots);
    }

    private sealed record TestContext(
        StudyActivityTracker Tracker,
        MemoryTimeline Timeline,
        FakeScreenshotCapture ScreenshotCapture);

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; init; }
    }

    private sealed class SessionRepository(StudySession active) : IStudySessionRepository
    {
        public Task AddAsync(SessionTransition started, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<StudySession?>(active);

        public Task SaveTransitionAsync(SessionTransition transition, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteAndCreditAsync(StudySessionCompletion completion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SettingsRepository(TrackingSettings settings) : ITrackingSettingsRepository
    {
        public Task<TrackingSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task SaveAsync(TrackingSettings updated, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ActivitySource(TimeSpan idleDuration) : IWindowsActivitySource
    {
        public ForegroundWindowSnapshot GetCurrent() => new(
            1,
            "Code",
            "TimeBet - Visual Studio Code",
            idleDuration,
            0,
            0,
            1280,
            720);
    }

    private sealed class FakeScreenshotCapture : IWindowsScreenshotCapture
    {
        public int CaptureCount { get; private set; }

        public CapturedScreenshot Capture(ForegroundWindowSnapshot window, string filePath)
        {
            CaptureCount++;
            return new CapturedScreenshot(filePath, window.Width, window.Height, 100);
        }
    }

    private sealed class MemoryTimeline : IActivityTimelineRepository
    {
        public List<ActivitySegment> Activities { get; } = [];

        public List<ScreenshotRecord> Screenshots { get; } = [];

        public Task AppendOrMergeActivityAsync(ActivitySegment segment, CancellationToken cancellationToken = default)
        {
            Activities.Add(segment);
            return Task.CompletedTask;
        }

        public Task AddScreenshotAsync(ScreenshotRecord screenshot, CancellationToken cancellationToken = default)
        {
            Screenshots.Add(screenshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TimelineSessionRecord>> GetSessionsAsync(DateTimeOffset startedBeforeUtc, DateTimeOffset endedAfterUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> DeleteScreenshotsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
