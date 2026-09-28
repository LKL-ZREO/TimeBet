using TimeBet.Application.Abstractions;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;

namespace TimeBet.Infrastructure.Tests;

public sealed class PlanningTaskServiceTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LinkedSessionCreditsActualTimeAndTaskCanBeCompleted()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var clock = new MutableClock(StartTime);
        var tasks = new TaskService(
            database.PlanningTasks,
            clock,
            database.PlanningTimeBlocks);
        var sessions = new StudySessionService(
            database.Sessions,
            database.Ledger,
            clock,
            database.PlanningTasks);

        var task = await tasks.CreateAsync(new CreateTaskRequest("Review calculus mistakes", 45));
        await sessions.StartAsync(new StartSessionRequest(task.Title, TaskId: task.Id));
        clock.UtcNow = StartTime.AddMinutes(20);
        await sessions.FinishAsync(new FinishSessionRequest("Reviewed chapter three", null));

        var afterFocus = await database.PlanningTasks.GetAsync(task.Id);
        Assert.NotNull(afterFocus);
        Assert.Equal(20 * 60, afterFocus.ActualSeconds);
        Assert.Equal(45 * 60, afterFocus.EstimatedSeconds);

        var completed = await tasks.SetCompletedAsync(new SetTaskCompletedRequest(task.Id, true));
        Assert.Equal("completed", completed.Status);
        Assert.DoesNotContain(await tasks.GetActiveAsync(), item => item.Id == task.Id);
    }

    [Fact]
    public async Task SessionCannotLinkToMissingTask()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var clock = new MutableClock(StartTime);
        var sessions = new StudySessionService(
            database.Sessions,
            database.Ledger,
            clock,
            database.PlanningTasks);

        await Assert.ThrowsAsync<TimeBet.Domain.DomainRuleException>(() =>
            sessions.StartAsync(new StartSessionRequest("Missing task", TaskId: Guid.NewGuid())));
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
