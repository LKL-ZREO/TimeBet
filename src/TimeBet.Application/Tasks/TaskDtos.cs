using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Tasks;

public sealed record CreateTaskRequest(
    string? Title,
    int? EstimatedMinutes = null,
    DateOnly? DueDate = null);

public sealed record SetTaskCompletedRequest(Guid Id, bool Completed);

public sealed record PlanningTaskSnapshot(
    Guid Id,
    string Title,
    int? EstimatedSeconds,
    long ActualSeconds,
    DateOnly? DueDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

internal static class TaskSnapshotMapper
{
    public static PlanningTaskSnapshot Map(PlanningTask task)
    {
        return new PlanningTaskSnapshot(
            task.Id,
            task.Title,
            task.EstimatedSeconds,
            task.ActualSeconds,
            task.DueDate,
            task.Status.ToString().ToLowerInvariant(),
            task.CreatedAtUtc,
            task.CompletedAtUtc);
    }
}
