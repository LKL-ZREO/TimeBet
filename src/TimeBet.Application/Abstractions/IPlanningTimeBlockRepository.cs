using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Abstractions;

public interface IPlanningTimeBlockRepository
{
    Task<IReadOnlyList<PlanningTimeBlock>> GetInRangeAsync(
        DateTimeOffset startsAtOrAfterUtc,
        DateTimeOffset startsBeforeUtc,
        CancellationToken cancellationToken = default);

    Task<PlanningTimeBlock?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> HasOverlapAsync(
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasPlannedForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task AddAsync(PlanningTimeBlock block, CancellationToken cancellationToken = default);

    Task MoveAsync(
        Guid id,
        DateTimeOffset startsAtUtc,
        int plannedSeconds,
        CancellationToken cancellationToken = default);

    Task DeletePlannedAsync(Guid id, CancellationToken cancellationToken = default);

    Task SkipAsync(Guid id, DateTimeOffset skippedAtUtc, CancellationToken cancellationToken = default);
}
