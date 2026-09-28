namespace TimeBet.Domain.Tasks;

public enum PlanningTimeBlockStatus
{
    Planned,
    Completed,
    Skipped,
}

public sealed record PlanningTimeBlock(
    Guid Id,
    Guid TaskId,
    DateTimeOffset StartsAtUtc,
    int PlannedSeconds,
    long ActualSeconds,
    PlanningTimeBlockStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc)
{
    public static PlanningTimeBlock Create(
        Guid taskId,
        DateTimeOffset startsAtUtc,
        int plannedSeconds,
        DateTimeOffset createdAtUtc,
        Guid? id = null)
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        }

        var blockId = id ?? Guid.NewGuid();
        if (blockId == Guid.Empty)
        {
            throw new ArgumentException("Time block ID cannot be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedSeconds);

        return new PlanningTimeBlock(
            blockId,
            taskId,
            DomainGuards.Utc(startsAtUtc, nameof(startsAtUtc)),
            plannedSeconds,
            0,
            PlanningTimeBlockStatus.Planned,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            null);
    }

    public static PlanningTimeBlock Restore(
        Guid id,
        Guid taskId,
        DateTimeOffset startsAtUtc,
        int plannedSeconds,
        long actualSeconds,
        PlanningTimeBlockStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? resolvedAtUtc)
    {
        if (id == Guid.Empty || taskId == Guid.Empty || plannedSeconds <= 0 || actualSeconds < 0)
        {
            throw new DomainRuleException("Time block data is invalid.");
        }

        if (status == PlanningTimeBlockStatus.Planned && resolvedAtUtc.HasValue ||
            status != PlanningTimeBlockStatus.Planned && !resolvedAtUtc.HasValue)
        {
            throw new DomainRuleException("Time block resolution state is invalid.");
        }

        if (status == PlanningTimeBlockStatus.Skipped && actualSeconds != 0)
        {
            throw new DomainRuleException("Skipped time blocks cannot contain actual time.");
        }

        return new PlanningTimeBlock(
            id,
            taskId,
            DomainGuards.Utc(startsAtUtc, nameof(startsAtUtc)),
            plannedSeconds,
            actualSeconds,
            status,
            DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc)),
            resolvedAtUtc.HasValue
                ? DomainGuards.Utc(resolvedAtUtc.Value, nameof(resolvedAtUtc))
                : null);
    }
}
