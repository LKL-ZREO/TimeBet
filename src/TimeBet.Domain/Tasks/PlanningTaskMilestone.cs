using TimeBet.Domain;

namespace TimeBet.Domain.Tasks;

public sealed record PlanningTaskMilestone(
    Guid Id,
    Guid TaskId,
    string Content,
    int SortOrder,
    bool IsCompleted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public static PlanningTaskMilestone Create(
        Guid taskId,
        string content,
        int sortOrder,
        DateTimeOffset createdAtUtc,
        Guid? id = null)
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);

        var milestoneId = id ?? Guid.NewGuid();
        if (milestoneId == Guid.Empty)
        {
            throw new ArgumentException("Milestone ID cannot be empty.", nameof(id));
        }

        return new PlanningTaskMilestone(
            milestoneId,
            taskId,
            DomainGuards.NonEmpty(content, nameof(content)),
            sortOrder,
            false,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            null);
    }

    public static PlanningTaskMilestone Restore(
        Guid id,
        Guid taskId,
        string content,
        int sortOrder,
        bool isCompleted,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        if (id == Guid.Empty || taskId == Guid.Empty || sortOrder < 0)
        {
            throw new DomainRuleException("Milestone data is invalid.");
        }

        if (isCompleted != completedAtUtc.HasValue)
        {
            throw new DomainRuleException("Milestone completion state is invalid.");
        }

        return new PlanningTaskMilestone(
            id,
            taskId,
            DomainGuards.NonEmpty(content, nameof(content)),
            sortOrder,
            isCompleted,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            completedAtUtc.HasValue
                ? DomainGuards.Utc(completedAtUtc.Value, nameof(completedAtUtc))
                : null);
    }

    public PlanningTaskMilestone SetCompleted(bool completed, DateTimeOffset changedAtUtc)
    {
        var changedAt = DomainGuards.Utc(changedAtUtc, nameof(changedAtUtc));
        return this with
        {
            IsCompleted = completed,
            CompletedAtUtc = completed ? changedAt : null,
        };
    }
}
