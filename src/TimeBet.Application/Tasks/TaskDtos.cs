using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Tasks;

public sealed record CreateTaskRequest(
    string? Title,
    int? EstimatedMinutes = null,
    DateOnly? DueDate = null);

public sealed record SetTaskCompletedRequest(Guid Id, bool Completed);

public sealed record AddTaskMilestoneRequest(Guid TaskId, string? Content);

public sealed record SetTaskMilestoneCompletedRequest(Guid Id, bool Completed);

public sealed record TaskMilestoneSnapshot(
    Guid Id,
    Guid TaskId,
    string Content,
    int SortOrder,
    bool IsCompleted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record PlanningTaskSnapshot(
    Guid Id,
    string Title,
    int? EstimatedSeconds,
    long ActualSeconds,
    DateOnly? DueDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<TaskMilestoneSnapshot> Milestones);

internal static class TaskSnapshotMapper
{
    public static PlanningTaskSnapshot Map(
        PlanningTask task,
        IReadOnlyList<PlanningTaskMilestone>? milestones = null)
    {
        return new PlanningTaskSnapshot(
            task.Id,
            task.Title,
            task.EstimatedSeconds,
            task.ActualSeconds,
            task.DueDate,
            task.Status.ToString().ToLowerInvariant(),
            task.CreatedAtUtc,
            task.CompletedAtUtc,
            (milestones ?? []).Select(MapMilestone).ToArray());
    }

    public static TaskMilestoneSnapshot MapMilestone(PlanningTaskMilestone milestone) =>
        new(
            milestone.Id,
            milestone.TaskId,
            milestone.Content,
            milestone.SortOrder,
            milestone.IsCompleted,
            milestone.CreatedAtUtc,
            milestone.CompletedAtUtc);
}
