using TimeBet.Application.Abstractions;
using TimeBet.Application.Analytics;
using TimeBet.Application.Sessions;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Timeline;

namespace TimeBet.Infrastructure.Tests;

public sealed class AnalyticsServiceTests
{
    [Fact]
    public async Task OverviewSplitsStudyAcrossMidnightAndExcludesPausedAndIdleTime()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 11, 23, 30, 0, TimeSpan.Zero));
        var sessions = new StudySessionService(database.Sessions, database.Ledger, clock);

        var started = await sessions.StartAsync(new StartSessionRequest("跨午夜复习"));
        var sessionId = started.Session!.Id;
        clock.UtcNow = new DateTimeOffset(2026, 8, 11, 23, 45, 0, TimeSpan.Zero);
        await sessions.PauseAsync();
        clock.UtcNow = new DateTimeOffset(2026, 8, 11, 23, 55, 0, TimeSpan.Zero);
        await sessions.ResumeAsync();
        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            sessionId,
            new DateTimeOffset(2026, 8, 11, 23, 50, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 12, 0, 10, 0, TimeSpan.Zero),
            "Code",
            "TimeBet"));
        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            sessionId,
            new DateTimeOffset(2026, 8, 12, 0, 10, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 12, 0, 30, 0, TimeSpan.Zero),
            "msedge",
            "资料"));
        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            sessionId,
            new DateTimeOffset(2026, 8, 12, 0, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 12, 0, 40, 0, TimeSpan.Zero),
            "idle",
            "空闲",
            isIdle: true));

        clock.UtcNow = new DateTimeOffset(2026, 8, 12, 0, 20, 0, TimeSpan.Zero);
        await sessions.FinishAsync(new FinishSessionRequest("完成复习", null));
        await database.Ledger.AppendAsync(LedgerEntry.CreateSpend(
            600,
            Guid.NewGuid().ToString("D"),
            "兑换奖励：电影",
            clock.UtcNow));

        clock.UtcNow = new DateTimeOffset(2026, 8, 12, 1, 0, 0, TimeSpan.Zero);
        var service = new AnalyticsService(database.Analytics, clock, TimeZoneInfo.Utc);
        var overview = await service.GetOverviewAsync();

        Assert.Equal(20 * 60, overview.TodayStudySeconds);
        Assert.Equal(40 * 60, overview.WeekStudySeconds);
        Assert.Equal(1, overview.TodayCompletedSessions);
        Assert.Equal(1, overview.WeekCompletedSessions);
        Assert.Equal(20 * 60, overview.DailyTrend[^2].StudySeconds);
        Assert.Equal(20 * 60, overview.DailyTrend[^1].StudySeconds);
        Assert.Equal(1800, overview.Ledger.BalanceSeconds);
        Assert.Equal(2400, overview.Ledger.EarnedSeconds);
        Assert.Equal(600, overview.Ledger.SpentSeconds);

        Assert.Collection(
            overview.ApplicationUsage,
            application =>
            {
                Assert.Equal("msedge", application.ProcessName);
                Assert.Equal(20 * 60, application.DurationSeconds);
                Assert.Equal(2d / 3d, application.Share, 6);
            },
            application =>
            {
                Assert.Equal("Code", application.ProcessName);
                Assert.Equal(10 * 60, application.DurationSeconds);
                Assert.Equal(1d / 3d, application.Share, 6);
            });
    }

    [Fact]
    public async Task CsvContainsAllLedgerEntriesAndEscapesNotesForExcel()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(
            1800,
            "补记,含\"引号\"",
            now));
        var service = new AnalyticsService(database.Analytics, new MutableClock(now), TimeZoneInfo.Utc);

        var csv = await service.CreateSummaryCsvAsync();

        Assert.Contains("TimeBet 学习摘要", csv, StringComparison.Ordinal);
        Assert.Contains("\"补记,含\"\"引号\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("adjustment,1800,manual", csv, StringComparison.Ordinal);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
