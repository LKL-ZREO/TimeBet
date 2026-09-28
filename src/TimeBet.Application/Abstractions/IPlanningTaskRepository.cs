using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Abstractions;

public interface IPlanningTaskRepository
{
    Task<IReadOnlyList<PlanningTask>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<PlanningTask?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(PlanningTask task, CancellationToken cancellationToken = default);

    Task SaveAsync(PlanningTask task, CancellationToken cancellationToken = default);
}
