using TimeBet.Application.Abstractions;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;
using TimeBet.Domain;

namespace TimeBet.Infrastructure.Tests;

public sealed class PlannerServiceTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TodayPlanCompletesWithActualFocusTime()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var clock = new MutableClock(StartTime);
        var taskService = new TaskService(
            database.PlanningTasks,
            clock,
            database.PlanningTimeBlocks);
        var planner = new PlannerService(
            database.PlanningTimeBlocks,
            database.PlanningTasks,
            clock,
            TimeZoneInfo.Utc);
        var sessions = new StudySessionService(
            database.Sessions,
            database.Ledger,
            clock,
            database.PlanningTasks,
            database.PlanningTimeBlocks);

        var task = await taskService.CreateAsync(new CreateTaskRequest(
            "Review calculus mistakes",
            45,
            new DateOnly(2026, 8, 19)));
        var block = await planner.CreateAsync(new CreateTimeBlockRequest(
            task.Id,
            StartTime.AddHours(1),
            45));

        var planned = await planner.GetTodayAsync();
        Assert.Equal(45 * 60, planned.PlannedSeconds);
        Assert.Equal(1, planned.PlannedCount);
        Assert.Equal(new DateOnly(2026, 8, 19), Assert.Single(planned.Blocks).DueDate);

        clock.UtcNow = StartTime.AddHours(1);
        await sessions.StartAsync(new StartSessionRequest(
            task.Title,
            TaskId: task.Id,
            TimeBlockId: block.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.SkipAsync(new SkipTimeBlockRequest(block.Id)));
        clock.UtcNow = StartTime.AddHours(1).AddMinutes(40);
        await sessions.FinishAsync(new FinishSessionRequest("Reviewed chapter three", null));

        var completed = await planner.GetTodayAsync();
        var completedBlock = Assert.Single(completed.Blocks);
        Assert.Equal("completed", completedBlock.Status);
        Assert.Equal(40 * 60, completedBlock.ActualSeconds);
        Assert.Equal(40 * 60, completed.ActualSeconds);
        Assert.Equal(1, completed.CompletedCount);

        var week = await planner.GetWeekAsync();
        Assert.Equal("2026-08-17", week.WeekStartedDate);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(45 * 60, week.PlannedSeconds);
        Assert.Equal(40 * 60, week.ActualSeconds);
        Assert.Equal(1, week.CompletedCount);
    }

    [Fact]
    public async Task OverlapIsRejectedAndSkippedBlockReleasesItsSlot()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var clock = new MutableClock(StartTime);
        var taskService = new TaskService(
            database.PlanningTasks,
            clock,
            database.PlanningTimeBlocks);
        var planner = new PlannerService(
            database.PlanningTimeBlocks,
            database.PlanningTasks,
            clock,
            TimeZoneInfo.Utc);
        var task = await taskService.CreateAsync(new CreateTaskRequest("Read papers", 60));
        var first = await planner.CreateAsync(new CreateTimeBlockRequest(
            task.Id,
            StartTime.AddHours(2),
            60));

        await Assert.ThrowsAsync<DomainRuleException>(() => planner.CreateAsync(
            new CreateTimeBlockRequest(task.Id, StartTime.AddHours(2).AddMinutes(30), 30)));
        await Assert.ThrowsAsync<DomainRuleException>(() => taskService.SetCompletedAsync(
            new SetTaskCompletedRequest(task.Id, true)));

        await planner.SkipAsync(new SkipTimeBlockRequest(first.Id));
        var replacement = await planner.CreateAsync(new CreateTimeBlockRequest(
            task.Id,
            StartTime.AddHours(2).AddMinutes(30),
            30));
        Assert.Equal("planned", replacement.Status);

        var moved = await planner.MoveAsync(new MoveTimeBlockRequest(
            replacement.Id,
            StartTime.AddHours(4),
            45));
        Assert.Equal(StartTime.AddHours(4), moved.StartsAtUtc);
        Assert.Equal(45 * 60, moved.PlannedSeconds);

        var deferred = await planner.DeferAsync(new DeferTimeBlockRequest(replacement.Id));
        Assert.Equal(StartTime.AddDays(1).AddHours(4), deferred.StartsAtUtc);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
