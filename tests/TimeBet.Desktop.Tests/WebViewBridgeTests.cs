using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Analytics;
using TimeBet.Application.Challenges;
using TimeBet.Application.Rewards;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;
using TimeBet.Application.Timeline;
using TimeBet.Desktop.WebView;
using TimeBet.Domain.Ledger;
using TimeBet.Infrastructure.Database;
using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop.Tests;

public sealed class WebViewBridgeTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task JsonWorkflowStartsAndFinishesSessionAtomically()
    {
        await using var context = await BridgeTestContext.CreateAsync();

        using var started = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "start-1",
              "method": "session.start",
              "payload": { "goal": "完成高数极限章节" }
            }
            """));
        Assert.True(started.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "running",
            started.RootElement.GetProperty("result").GetProperty("session").GetProperty("status").GetString());

        context.Clock.UtcNow = StartTime.AddSeconds(65);
        using var finished = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "finish-1",
              "method": "session.finish",
              "payload": {
                "completionNote": "完成例题并整理错题",
                "tasks": [{ "content": "完成 12 道例题", "isCompleted": true }]
              }
            }
            """));

        var root = finished.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(65, root.GetProperty("result").GetProperty("state").GetProperty("balanceSeconds").GetInt64());
        Assert.Equal(65, await context.Ledger.GetBalanceSecondsAsync());
        Assert.Null(await context.Sessions.GetActiveAsync());
        Assert.Equal(1, await context.CountTasksAsync());
    }

    [Fact]
    public async Task UnknownMethodReturnsStructuredError()
    {
        await using var context = await BridgeTestContext.CreateAsync();

        using var response = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "bad-1", "method": "system.deleteEverything", "payload": {} }
            """));

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-1", response.RootElement.GetProperty("id").GetString());
        Assert.Equal(
            "unknown_method",
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task TimelineCommandReturnsPrivacySafeDefaults()
    {
        await using var context = await BridgeTestContext.CreateAsync();

        using var response = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "timeline-1", "method": "timeline.today" }
            """));

        var root = response.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("result").GetProperty("settings").GetProperty("captureActivity").GetBoolean());
        Assert.False(root.GetProperty("result").GetProperty("settings").GetProperty("captureScreenshots").GetBoolean());
        Assert.Empty(root.GetProperty("result").GetProperty("timeline").GetProperty("sessions").EnumerateArray());
    }

    [Fact]
    public async Task RelevanceCommandUsesRecentBackgroundWindowEvidence()
    {
        await using var context = await BridgeTestContext.CreateAsync();
        await context.Bridge.HandleMessageAsync("""
            {
              "id": "start-relevance",
              "method": "session.start",
              "payload": { "goal": "复习高等数学极限章节" }
            }
            """);
        context.Evidence.Observe(
            new ForegroundWindowSnapshot(
                nint.Zero,
                "WINWORD",
                "高等数学极限错题.docx - Word",
                TimeSpan.Zero,
                0,
                0,
                1200,
                800),
            DateTimeOffset.UtcNow);

        using var response = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "relevance-1", "method": "session.relevance.current" }
            """));

        var result = response.RootElement.GetProperty("result");
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("high", result.GetProperty("level").GetString());
        Assert.Equal("WINWORD", result.GetProperty("processName").GetString());
        Assert.True(result.GetProperty("score").GetInt32() >= 80);
    }

    [Fact]
    public async Task TaskCommandsCreateListAndLinkFocusTime()
    {
        await using var context = await BridgeTestContext.CreateAsync();

        using var created = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "task-create-1",
              "method": "task.create",
              "payload": { "title": "Review calculus mistakes", "estimatedMinutes": 30 }
            }
            """));
        var taskId = created.RootElement.GetProperty("result").GetProperty("id").GetGuid();

        using var started = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "task-start-1",
              "method": "session.start",
              "payload": { "goal": "Review calculus mistakes", "taskId": "{{taskId:D}}" }
            }
            """));
        Assert.Equal(
            taskId,
            started.RootElement.GetProperty("result").GetProperty("session").GetProperty("taskId").GetGuid());

        context.Clock.UtcNow = StartTime.AddSeconds(65);
        using var finished = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "task-finish-1",
              "method": "session.finish",
              "payload": { "completionNote": "Reviewed chapter three" }
            }
            """));
        Assert.True(finished.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(65, (await context.PlanningTasks.GetAsync(taskId))?.ActualSeconds);

        using var completed = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "task-complete-1",
              "method": "task.set-completed",
              "payload": { "id": "{{taskId:D}}", "completed": true }
            }
            """));
        Assert.Equal("completed", completed.RootElement.GetProperty("result").GetProperty("status").GetString());

        using var listed = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "task-list-1", "method": "task.list" }
            """));
        Assert.Empty(listed.RootElement.GetProperty("result").EnumerateArray());
    }

    [Fact]
    public async Task PlannerCommandsScheduleAndCompleteATimeBlock()
    {
        await using var context = await BridgeTestContext.CreateAsync();
        using var createdTask = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "planner-task-1",
              "method": "task.create",
              "payload": {
                "title": "Write thesis outline",
                "estimatedMinutes": 45,
                "dueDate": "2026-08-12"
              }
            }
            """));
        var taskId = createdTask.RootElement.GetProperty("result").GetProperty("id").GetGuid();

        using var createdBlock = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "planner-block-1",
              "method": "timeblock.create",
              "payload": {
                "taskId": "{{taskId:D}}",
                "startsAtUtc": "2026-08-12T09:00:00Z",
                "plannedMinutes": 45
              }
            }
            """));
        var blockId = createdBlock.RootElement.GetProperty("result").GetProperty("id").GetGuid();

        using var movedBlock = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "planner-move-1",
              "method": "timeblock.move",
              "payload": {
                "id": "{{blockId:D}}",
                "startsAtUtc": "2026-08-12T09:15:00Z",
                "plannedMinutes": 45
              }
            }
            """));
        Assert.Equal(
            "2026-08-12T09:15:00+00:00",
            movedBlock.RootElement.GetProperty("result").GetProperty("startsAtUtc").GetString());

        using var today = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "planner-today-1", "method": "planner.today" }
            """));
        Assert.Equal(45 * 60, today.RootElement.GetProperty("result").GetProperty("plannedSeconds").GetInt64());
        Assert.Single(today.RootElement.GetProperty("result").GetProperty("blocks").EnumerateArray());
        using var week = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "planner-week-1", "method": "planner.week" }
            """));
        Assert.Equal(7, week.RootElement.GetProperty("result").GetProperty("days").GetArrayLength());

        context.Clock.UtcNow = StartTime.AddHours(1);
        using var started = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "planner-start-1",
              "method": "session.start",
              "payload": {
                "goal": "Write thesis outline",
                "taskId": "{{taskId:D}}",
                "timeBlockId": "{{blockId:D}}"
              }
            }
            """));
        Assert.Equal(
            blockId,
            started.RootElement.GetProperty("result").GetProperty("session").GetProperty("timeBlockId").GetGuid());

        context.Clock.UtcNow = StartTime.AddHours(1).AddMinutes(35);
        using var finished = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "planner-finish-1",
              "method": "session.finish",
              "payload": { "completionNote": "Outline completed" }
            }
            """));
        Assert.True(finished.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(35 * 60, (await context.PlanningTimeBlocks.GetAsync(blockId))?.ActualSeconds);
    }

    [Fact]
    public async Task RewardCommandsCreateAndRedeemAgainstTheLedger()
    {
        await using var context = await BridgeTestContext.CreateAsync();
        await context.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));

        using var created = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "reward-create-1",
              "method": "reward.create",
              "payload": {
                "name": "一杯奶茶",
                "description": "完成一章后兑换",
                "costSeconds": 1800
              }
            }
            """));
        var reward = Assert.Single(created.RootElement.GetProperty("result").GetProperty("rewards").EnumerateArray());
        var rewardId = reward.GetProperty("id").GetString();

        using var redeemed = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "reward-redeem-1",
              "method": "reward.redeem",
              "payload": { "id": "{{rewardId}}" }
            }
            """));

        var result = redeemed.RootElement.GetProperty("result");
        Assert.True(redeemed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1800, result.GetProperty("catalog").GetProperty("balanceSeconds").GetInt64());
        Assert.Equal("一杯奶茶", result.GetProperty("redemption").GetProperty("rewardName").GetString());
        Assert.Equal(1800, await context.Ledger.GetBalanceSecondsAsync());
        Assert.Single(await context.Rewards.GetRecentRedemptionsAsync(8));
    }

    [Fact]
    public async Task AnalyticsCommandsReturnOverviewLedgerAndCancelledExport()
    {
        await using var context = await BridgeTestContext.CreateAsync();
        await context.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(900, "测试统计", StartTime));

        using var overview = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "analytics-1", "method": "analytics.overview" }
            """));
        using var entries = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "ledger-1", "method": "ledger.entries" }
            """));
        using var exported = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "export-1", "method": "export.summary" }
            """));

        Assert.True(overview.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            900,
            overview.RootElement.GetProperty("result").GetProperty("ledger").GetProperty("balanceSeconds").GetInt64());
        Assert.Single(entries.RootElement.GetProperty("result").EnumerateArray());
        Assert.False(exported.RootElement.GetProperty("result").GetProperty("saved").GetBoolean());
    }

    [Fact]
    public async Task ChallengeCommandsRequireOptInAndSettleNetResult()
    {
        await using var context = await BridgeTestContext.CreateAsync();
        await context.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试星愿", StartTime));

        using var initial = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            { "id": "challenge-overview-1", "method": "challenge.overview" }
            """));
        Assert.False(initial.RootElement.GetProperty("result").GetProperty("enabled").GetBoolean());
        Assert.Equal(
            5,
            initial.RootElement.GetProperty("result").GetProperty("probabilityTiers").GetArrayLength());

        using var enabled = JsonDocument.Parse(await context.Bridge.HandleMessageAsync("""
            {
              "id": "challenge-enable-1",
              "method": "challenge.settings.update",
              "payload": { "enabled": true }
            }
            """));
        Assert.True(enabled.RootElement.GetProperty("result").GetProperty("enabled").GetBoolean());

        var roundId = Guid.NewGuid();
        using var played = JsonDocument.Parse(await context.Bridge.HandleMessageAsync($$"""
            {
              "id": "challenge-play-1",
              "method": "challenge.play",
              "payload": { "roundId": "{{roundId:D}}", "wagerSeconds": 300 }
            }
            """));
        var result = played.RootElement.GetProperty("result");
        Assert.True(played.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("small-fortune", result.GetProperty("round").GetProperty("outcomeCode").GetString());
        Assert.Equal(150, result.GetProperty("round").GetProperty("netSeconds").GetInt64());
        Assert.Equal(3750, result.GetProperty("overview").GetProperty("balanceSeconds").GetInt64());
        Assert.Equal(3750, await context.Ledger.GetBalanceSecondsAsync());
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = StartTime;
    }

    private sealed class BridgeTestContext : IAsyncDisposable
    {
        private readonly string directoryPath;

        private BridgeTestContext(string directoryPath)
        {
            this.directoryPath = directoryPath;
            var factory = new SqliteConnectionFactory(Path.Combine(directoryPath, "timebet.db"));
            Factory = factory;
            Sessions = new SqliteStudySessionRepository(factory);
            Ledger = new SqliteLedgerRepository(factory);
            var timelineRepository = new SqliteActivityTimelineRepository(factory);
            var settings = new SqliteTrackingSettingsRepository(factory);
            var rewards = new SqliteRewardRepository(factory);
            var analytics = new SqliteAnalyticsRepository(factory);
            var planningTasks = new SqlitePlanningTaskRepository(factory);
            var planningTimeBlocks = new SqlitePlanningTimeBlockRepository(factory);
            Rewards = rewards;
            PlanningTasks = planningTasks;
            PlanningTimeBlocks = planningTimeBlocks;
            Clock = new MutableClock();
            Evidence = new ActivityEvidenceBuffer();
            Bridge = new WebViewBridge(
                new StudySessionService(
                    Sessions,
                    Ledger,
                    Clock,
                    planningTasks,
                    planningTimeBlocks),
                new TimelineService(timelineRepository, settings, Path.Combine(directoryPath, "Screenshots")),
                new RewardService(rewards, Ledger, Clock),
                new AnalyticsService(analytics, Clock, TimeZoneInfo.Utc),
                new ChallengeService(
                    new SqliteChallengeRepository(factory),
                    Clock,
                    new FixedRandom(0.60),
                    TimeZoneInfo.Utc),
                planningTasks: new TaskService(planningTasks, Clock, planningTimeBlocks),
                planner: new PlannerService(
                    planningTimeBlocks,
                    planningTasks,
                    Clock,
                    TimeZoneInfo.Utc),
                activityEvidence: Evidence);
        }

        public SqliteConnectionFactory Factory { get; }

        public SqliteStudySessionRepository Sessions { get; }

        public SqliteLedgerRepository Ledger { get; }

        public SqliteRewardRepository Rewards { get; }

        public SqlitePlanningTaskRepository PlanningTasks { get; }

        public SqlitePlanningTimeBlockRepository PlanningTimeBlocks { get; }

        public MutableClock Clock { get; }

        public ActivityEvidenceBuffer Evidence { get; }

        public WebViewBridge Bridge { get; }

        private sealed class FixedRandom(double outcome) : IRandomSource
        {
            public double NextUnitInterval() => outcome;
        }

        public static async Task<BridgeTestContext> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "TimeBet.Bridge.Tests", Guid.NewGuid().ToString("N"));
            var context = new BridgeTestContext(directory);
            await new TimeBetDatabase(context.Factory).InitializeAsync();
            return context;
        }

        public async Task<long> CountTasksAsync()
        {
            await using var connection = await Factory.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM session_tasks;";
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
