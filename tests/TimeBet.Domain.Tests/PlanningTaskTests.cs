using TimeBet.Domain;
using TimeBet.Domain.Tasks;

namespace TimeBet.Domain.Tests;

public sealed class PlanningTaskTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAndCompletePreservePlanningDurations()
    {
        var task = PlanningTask.Create("Review calculus mistakes", CreatedAt, 45 * 60);

        var completed = task.Complete(CreatedAt.AddHours(1));

        Assert.Equal(45 * 60, completed.EstimatedSeconds);
        Assert.Equal(PlanningTaskStatus.Completed, completed.Status);
        Assert.Equal(CreatedAt.AddHours(1), completed.CompletedAtUtc);
    }

    [Fact]
    public void ArchivedOrChronologicallyInvalidTasksCannotBeCompleted()
    {
        var task = PlanningTask.Create("Write outline", CreatedAt);
        var archived = task with { Status = PlanningTaskStatus.Archived };

        Assert.Throws<DomainRuleException>(() => archived.Complete(CreatedAt.AddMinutes(1)));
        Assert.Throws<DomainRuleException>(() => task.Complete(CreatedAt.AddSeconds(-1)));
    }

    [Fact]
    public void TimeBlockRequiresPositiveDurationAndConsistentResolution()
    {
        var taskId = Guid.NewGuid();
        var block = PlanningTimeBlock.Create(
            taskId,
            CreatedAt.AddHours(1),
            30 * 60,
            CreatedAt);

        Assert.Equal(PlanningTimeBlockStatus.Planned, block.Status);
        Assert.Equal(30 * 60, block.PlannedSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanningTimeBlock.Create(taskId, CreatedAt, 0, CreatedAt));
        Assert.Throws<DomainRuleException>(() => PlanningTimeBlock.Restore(
            block.Id,
            taskId,
            block.StartsAtUtc,
            block.PlannedSeconds,
            60,
            PlanningTimeBlockStatus.Skipped,
            CreatedAt,
            CreatedAt.AddMinutes(1)));
    }
}
