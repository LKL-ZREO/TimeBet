using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Tasks;

public sealed class TaskService
{
    private readonly IPlanningTaskRepository tasks;
    private readonly IClock clock;
    private readonly IPlanningTimeBlockRepository? timeBlocks;

    public TaskService(
        IPlanningTaskRepository tasks,
        IClock clock,
        IPlanningTimeBlockRepository? timeBlocks = null)
    {
        this.tasks = tasks;
        this.clock = clock;
        this.timeBlocks = timeBlocks;
    }

    public async Task<IReadOnlyList<PlanningTaskSnapshot>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await tasks.GetActiveAsync(cancellationToken);
        return items.Select(TaskSnapshotMapper.Map).ToArray();
    }

    public async Task<PlanningTaskSnapshot> CreateAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EstimatedMinutes is <= 0 or > 10_080)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Estimated task time must be between 1 minute and 7 days.");
        }

        int? estimatedSeconds = request.EstimatedMinutes.HasValue
            ? checked(request.EstimatedMinutes.Value * 60)
            : null;
        var task = PlanningTask.Create(
            request.Title ?? string.Empty,
            clock.UtcNow,
            estimatedSeconds,
            request.DueDate);
        await tasks.AddAsync(task, cancellationToken);
        return TaskSnapshotMapper.Map(task);
    }

    public async Task<PlanningTaskSnapshot> SetCompletedAsync(
        SetTaskCompletedRequest request,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("Task does not exist.");
        if (request.Completed && task.Status == PlanningTaskStatus.Completed ||
            !request.Completed && task.Status == PlanningTaskStatus.Active)
        {
            return TaskSnapshotMapper.Map(task);
        }

        if (request.Completed &&
            timeBlocks is not null &&
            await timeBlocks.HasPlannedForTaskAsync(task.Id, cancellationToken))
        {
            throw new DomainRuleException("Skip or complete planned time blocks before completing this task.");
        }

        var updated = request.Completed ? task.Complete(clock.UtcNow) : task.Reopen();
        await tasks.SaveAsync(updated, cancellationToken);
        return TaskSnapshotMapper.Map(updated);
    }
}
