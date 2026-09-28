using Microsoft.Data.Sqlite;
using TimeBet.Infrastructure.Database;

namespace TimeBet.Infrastructure.Tests;

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string directoryPath;

    public TemporaryDatabase()
    {
        directoryPath = Path.Combine(Path.GetTempPath(), "TimeBet.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "timebet.db");
        Factory = new SqliteConnectionFactory(databasePath);
        Database = new TimeBetDatabase(Factory);
        Sessions = new SqliteStudySessionRepository(Factory);
        Ledger = new SqliteLedgerRepository(Factory);
        Timeline = new SqliteActivityTimelineRepository(Factory);
        TrackingSettings = new SqliteTrackingSettingsRepository(Factory);
        Rewards = new SqliteRewardRepository(Factory);
        Analytics = new SqliteAnalyticsRepository(Factory);
        Challenges = new SqliteChallengeRepository(Factory);
        PlanningTasks = new SqlitePlanningTaskRepository(Factory);
        PlanningTimeBlocks = new SqlitePlanningTimeBlockRepository(Factory);
    }

    public SqliteConnectionFactory Factory { get; }

    public TimeBetDatabase Database { get; }

    public SqliteStudySessionRepository Sessions { get; }

    public SqliteLedgerRepository Ledger { get; }

    public SqliteActivityTimelineRepository Timeline { get; }

    public SqliteTrackingSettingsRepository TrackingSettings { get; }

    public SqliteRewardRepository Rewards { get; }

    public SqliteAnalyticsRepository Analytics { get; }

    public SqliteChallengeRepository Challenges { get; }

    public SqlitePlanningTaskRepository PlanningTasks { get; }

    public SqlitePlanningTimeBlockRepository PlanningTimeBlocks { get; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Database.InitializeAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
