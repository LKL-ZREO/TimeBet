using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Tasks;

public sealed class TaskService
{
    private readonly IPlanningTaskRepository tasks;
    private readonly IClock clock;
    private readonly IPlanningTimeBlockRepository? timeBlocks;
    private readonly IPlanningTaskMilestoneRepository? milestones;

    public TaskService(
        IPlanningTaskRepository tasks,
        IClock clock,
        IPlanningTimeBlockRepository? timeBlocks = null,
        IPlanningTaskMilestoneRepository? milestones = null)
    {
        this.tasks = tasks;
        this.clock = clock;
        this.timeBlocks = timeBlocks;
        this.milestones = milestones;
    }

    public async Task<IReadOnlyList<PlanningTaskSnapshot>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await tasks.GetActiveAsync(cancellationToken);
        var snapshots = new List<PlanningTaskSnapshot>(items.Count);
        foreach (var item in items)
        {
            snapshots.Add(TaskSnapshotMapper.Map(
                item,
                milestones is null
                    ? []
                    : await milestones.GetForTaskAsync(item.Id, cancellationToken)));
        }

        return snapshots;
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
        return TaskSnapshotMapper.Map(task, []);
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
            return await MapAsync(task, cancellationToken);
        }

        if (request.Completed &&
            timeBlocks is not null &&
            await timeBlocks.HasPlannedForTaskAsync(task.Id, cancellationToken))
        {
            throw new DomainRuleException("Skip or complete planned time blocks before completing this task.");
        }

        var updated = request.Completed ? task.Complete(clock.UtcNow) : task.Reopen();
        await tasks.SaveAsync(updated, cancellationToken);
        return await MapAsync(updated, cancellationToken);
    }

    public async Task<TaskMilestoneSnapshot> AddMilestoneAsync(
        AddTaskMilestoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var repository = milestones
            ?? throw new InvalidOperationException("Task milestones are unavailable.");
        var task = await tasks.GetAsync(request.TaskId, cancellationToken)
            ?? throw new DomainRuleException("Task does not exist.");
        if (task.Status != PlanningTaskStatus.Active)
        {
            throw new DomainRuleException("Only active tasks can receive milestones.");
        }

        var existing = await repository.GetForTaskAsync(task.Id, cancellationToken);
        var milestone = PlanningTaskMilestone.Create(
            task.Id,
            request.Content ?? string.Empty,
            existing.Count,
            clock.UtcNow);
        await repository.AddAsync(milestone, cancellationToken);
        return TaskSnapshotMapper.MapMilestone(milestone);
    }

    public async Task<TaskMilestoneSnapshot> SetMilestoneCompletedAsync(
        SetTaskMilestoneCompletedRequest request,
        CancellationToken cancellationToken = default)
    {
        var repository = milestones
            ?? throw new InvalidOperationException("Task milestones are unavailable.");
        var milestone = await repository.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("Milestone does not exist.");
        if (milestone.IsCompleted == request.Completed)
        {
            return TaskSnapshotMapper.MapMilestone(milestone);
        }

        var updated = milestone.SetCompleted(request.Completed, clock.UtcNow);
        await repository.SaveAsync(updated, cancellationToken);
        return TaskSnapshotMapper.MapMilestone(updated);
    }

    private async Task<PlanningTaskSnapshot> MapAsync(
        PlanningTask task,
        CancellationToken cancellationToken)
    {
        return TaskSnapshotMapper.Map(
            task,
            milestones is null
                ? []
                : await milestones.GetForTaskAsync(task.Id, cancellationToken));
    }
}
