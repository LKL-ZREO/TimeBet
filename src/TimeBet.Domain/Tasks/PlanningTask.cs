using TimeBet.Domain;

namespace TimeBet.Domain.Tasks;

public enum PlanningTaskStatus
{
    Active,
    Completed,
    Archived,
}

public sealed record PlanningTask(
    Guid Id,
    string Title,
    int? EstimatedSeconds,
    long ActualSeconds,
    DateOnly? DueDate,
    PlanningTaskStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public static PlanningTask Create(
        string title,
        DateTimeOffset createdAtUtc,
        int? estimatedSeconds = null,
        DateOnly? dueDate = null,
        Guid? id = null)
    {
        var taskId = id ?? Guid.NewGuid();
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("Task ID cannot be empty.", nameof(id));
        }

        if (estimatedSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedSeconds),
                estimatedSeconds,
                "Estimated task time must be positive.");
        }

        return new PlanningTask(
            taskId,
            DomainGuards.NonEmpty(title, nameof(title)),
            estimatedSeconds,
            0,
            dueDate,
            PlanningTaskStatus.Active,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            null);
    }

    public static PlanningTask Restore(
        Guid id,
        string title,
        int? estimatedSeconds,
        long actualSeconds,
        DateOnly? dueDate,
        PlanningTaskStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Task ID cannot be empty.", nameof(id));
        }

        if (actualSeconds < 0 || estimatedSeconds is <= 0)
        {
            throw new DomainRuleException("Task duration is invalid.");
        }

        if (status == PlanningTaskStatus.Completed && !completedAtUtc.HasValue)
        {
            throw new DomainRuleException("Completed tasks require a completion time.");
        }

        if (status != PlanningTaskStatus.Completed && completedAtUtc.HasValue)
        {
            throw new DomainRuleException("Only completed tasks can have a completion time.");
        }

        return new PlanningTask(
            id,
            DomainGuards.NonEmpty(title, nameof(title)),
            estimatedSeconds,
            actualSeconds,
            dueDate,
            status,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            completedAtUtc.HasValue
                ? DomainGuards.Utc(completedAtUtc.Value, nameof(completedAtUtc))
                : null);
    }

    public PlanningTask Complete(DateTimeOffset completedAtUtc)
    {
        if (Status == PlanningTaskStatus.Archived)
        {
            throw new DomainRuleException("Archived tasks cannot be completed.");
        }

        var completedAt = DomainGuards.Utc(completedAtUtc, nameof(completedAtUtc));
        if (completedAt < CreatedAtUtc)
        {
            throw new DomainRuleException("Task completion cannot precede task creation.");
        }

        return this with
        {
            Status = PlanningTaskStatus.Completed,
            CompletedAtUtc = completedAt,
        };
    }

    public PlanningTask Reopen()
    {
        if (Status == PlanningTaskStatus.Archived)
        {
            throw new DomainRuleException("Archived tasks cannot be reopened.");
        }

        return this with
        {
            Status = PlanningTaskStatus.Active,
            CompletedAtUtc = null,
        };
    }
}
