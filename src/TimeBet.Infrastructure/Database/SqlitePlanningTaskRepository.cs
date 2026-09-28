using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Tasks;

namespace TimeBet.Infrastructure.Database;

public sealed class SqlitePlanningTaskRepository : IPlanningTaskRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqlitePlanningTaskRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PlanningTask>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE status = 'active' ORDER BY created_at_utc_ms, id;";
        return await ReadManyAsync(command, cancellationToken);
    }

    public async Task<PlanningTask?> GetAsync(
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

    public async Task AddAsync(PlanningTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planning_tasks
                (id, title, estimated_seconds, actual_seconds, due_date, status, created_at_utc_ms, completed_at_utc_ms)
            VALUES
                ($id, $title, $estimatedSeconds, $actualSeconds, $dueDate, $status, $createdAt, $completedAt);
            """;
        AddParameters(command, task);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(PlanningTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE planning_tasks
            SET title = $title,
                estimated_seconds = $estimatedSeconds,
                actual_seconds = $actualSeconds,
                due_date = $dueDate,
                status = $status,
                completed_at_utc_ms = $completedAt
            WHERE id = $id;
            """;
        AddParameters(command, task);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The planning task no longer exists.");
        }
    }

    private static readonly string BaseSelect = """
        SELECT id, title, estimated_seconds, actual_seconds, due_date, status, created_at_utc_ms, completed_at_utc_ms
        FROM planning_tasks
        """;

    private static async Task<IReadOnlyList<PlanningTask>> ReadManyAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tasks = new List<PlanningTask>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(Read(reader));
        }

        return tasks;
    }

    private static PlanningTask Read(SqliteDataReader reader)
    {
        return PlanningTask.Restore(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.GetInt64(3),
            reader.IsDBNull(4)
                ? null
                : DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Enum.Parse<PlanningTaskStatus>(reader.GetString(5), ignoreCase: true),
            SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : SqliteValueConverters.FromUnixMilliseconds(reader.GetInt64(7)));
    }

    private static void AddParameters(SqliteCommand command, PlanningTask task)
    {
        command.Parameters.AddWithValue("$id", task.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$estimatedSeconds", task.EstimatedSeconds.HasValue ? task.EstimatedSeconds.Value : DBNull.Value);
        command.Parameters.AddWithValue("$actualSeconds", task.ActualSeconds);
        command.Parameters.AddWithValue("$dueDate", task.DueDate.HasValue
            ? task.DueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DBNull.Value);
        command.Parameters.AddWithValue("$status", task.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$createdAt", SqliteValueConverters.ToUnixMilliseconds(task.CreatedAtUtc));
        command.Parameters.AddWithValue("$completedAt", task.CompletedAtUtc.HasValue
            ? SqliteValueConverters.ToUnixMilliseconds(task.CompletedAtUtc.Value)
            : DBNull.Value);
    }
}
