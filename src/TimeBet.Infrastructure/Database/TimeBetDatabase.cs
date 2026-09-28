namespace TimeBet.Infrastructure.Database;

public sealed class TimeBetDatabase
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteMigrationRunner migrationRunner;

    public TimeBetDatabase(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
        migrationRunner = new SqliteMigrationRunner();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await migrationRunner.ApplyAsync(connection, cancellationToken);
    }
}
