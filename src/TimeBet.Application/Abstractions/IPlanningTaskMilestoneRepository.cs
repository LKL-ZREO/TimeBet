using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Abstractions;

public interface IPlanningTaskMilestoneRepository
{
    Task<IReadOnlyList<PlanningTaskMilestone>> GetForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<PlanningTaskMilestone?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PlanningTaskMilestone milestone,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PlanningTaskMilestone milestone,
        CancellationToken cancellationToken = default);
}
