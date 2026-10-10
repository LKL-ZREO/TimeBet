using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Tasks;

namespace TimeBet.Infrastructure.Database;

public sealed class SqlitePlanningTaskMilestoneRepository : IPlanningTaskMilestoneRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePlanningTaskMilestoneRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PlanningTaskMilestone>> GetForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + "\n" + """
            WHERE task_id = $taskId
            ORDER BY sort_order, created_at_utc_ms, id;
            """;
        command.Parameters.AddWithValue("$taskId", taskId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<PlanningTaskMilestone>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        return items;
    }

    public async Task<PlanningTaskMilestone?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task AddAsync(
        PlanningTaskMilestone milestone,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planning_task_milestones
                (id, task_id, content, sort_order, is_completed, created_at_utc_ms, completed_at_utc_ms)
            VALUES
                ($id, $taskId, $content, $sortOrder, 0, $createdAt, NULL);
            """;
        AddParameters(command, milestone);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        PlanningTaskMilestone milestone,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE planning_task_milestones
            SET content = $content,
                sort_order = $sortOrder,
                is_completed = $isCompleted,
                completed_at_utc_ms = $completedAt
            WHERE id = $id AND task_id = $taskId;
            """;
        AddParameters(command, milestone);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The milestone no longer exists.");
        }
    }

    private const string BaseSelect = """
        SELECT id, task_id, content, sort_order, is_completed,
               created_at_utc_ms, completed_at_utc_ms
        FROM planning_task_milestones
        """;

    private static PlanningTaskMilestone Read(SqliteDataReader reader)
    {
        return PlanningTaskMilestone.Restore(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetBoolean(4),
            SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6)
                ? null
                : SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(6)));
    }

    private static void AddParameters(
        SqliteCommand command,
        PlanningTaskMilestone milestone)
    {
        command.Parameters.AddWithValue("$id", milestone.Id.ToString("D"));
        command.Parameters.AddWithValue("$taskId", milestone.TaskId.ToString("D"));
        command.Parameters.AddWithValue("$content", milestone.Content);
        command.Parameters.AddWithValue("$sortOrder", milestone.SortOrder);
        command.Parameters.AddWithValue("$isCompleted", milestone.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue(
            "$createdAt",
            SqliteValueConverters.ToUnixMilliseconds(milestone.CreatedAtUtc));
        command.Parameters.AddWithValue(
            "$completedAt",
            milestone.CompletedAtUtc.HasValue
                ? SqliteValueConverters.ToUnixMilliseconds(milestone.CompletedAtUtc.Value)
                : DBNull.Value);
    }
}
