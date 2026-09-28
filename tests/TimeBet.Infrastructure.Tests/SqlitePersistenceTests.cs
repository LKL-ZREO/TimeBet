using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Sessions;
using TimeBet.Domain.Timeline;

namespace TimeBet.Infrastructure.Tests;

public sealed class SqlitePersistenceTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MigrationsAreIdempotentAndEnableWal()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var connection = await database.Factory.OpenAsync();
        await using var migrationCount = connection.CreateCommand();
        migrationCount.CommandText = "SELECT COUNT(*) FROM __timebet_migrations;";
        Assert.Equal(7L, Convert.ToInt64(await migrationCount.ExecuteScalarAsync(), CultureInfo.InvariantCulture));

        await using var journalMode = connection.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (await journalMode.ExecuteScalarAsync())?.ToString());

        await using var challengeEnabled = connection.CreateCommand();
        challengeEnabled.CommandText = "SELECT enabled FROM challenge_settings WHERE singleton_id = 1;";
        Assert.Equal(
            0L,
            Convert.ToInt64(
                await challengeEnabled.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ExistingPhaseFourDatabaseReceivesSessionScreenshotConsentColumn()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        await using var connection = await database.Factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('study_sessions')
            WHERE name = 'capture_screenshots';
            """;
        Assert.Equal(
            1L,
            Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));

        await using var migration = connection.CreateCommand();
        migration.CommandText = """
            SELECT migration_id
            FROM __timebet_migrations
            WHERE migration_name = '003_reward_catalog.sql';
            """;
        Assert.Equal(
            3L,
            Convert.ToInt64(await migration.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MigrationThreeHandlesExistingScreenshotConsentColumn()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        await using (var connection = await database.Factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM __timebet_migrations WHERE migration_id = 3;";
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        await using var verify = await database.Factory.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM __timebet_migrations;";
        Assert.Equal(
            7L,
            Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MigrationThreeAddsMissingScreenshotConsentColumn()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        await using (var connection = await database.Factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM __timebet_migrations WHERE migration_id = 3;
                ALTER TABLE study_sessions DROP COLUMN capture_screenshots;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        await using var verify = await database.Factory.OpenAsync();
        await using var column = verify.CreateCommand();
        column.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('study_sessions')
            WHERE name = 'capture_screenshots';
            """;
        Assert.Equal(
            1L,
            Convert.ToInt64(await column.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ExistingPhaseSixDatabaseReceivesDisabledChallengeSettings()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        await using (var connection = await database.Factory.OpenAsync())
        {
            await using var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                DELETE FROM __timebet_migrations WHERE migration_id = 4;
                DROP TABLE challenge_settings;
                DROP INDEX ix_game_rounds_played_at;
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        await using var verify = await database.Factory.OpenAsync();
        await using var command = verify.CreateCommand();
        command.CommandText = """
            SELECT m.migration_id, s.enabled
            FROM __timebet_migrations AS m
            CROSS JOIN challenge_settings AS s
            WHERE m.migration_name = '004_challenge_settings.sql'
              AND s.singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(4, reader.GetInt32(0));
        Assert.False(reader.GetBoolean(1));
    }

    [Fact]
    public async Task TrackingSettingsDefaultToActivityOnlyAndPersistUpdates()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        var defaults = await database.TrackingSettings.GetAsync();
        Assert.True(defaults.CaptureActivity);
        Assert.False(defaults.CaptureScreenshots);
        Assert.Equal(5, defaults.ScreenshotIntervalMinutes);
        Assert.Equal(7, defaults.ScreenshotRetentionDays);

        var updated = defaults with
        {
            CaptureScreenshots = true,
            ScreenshotIntervalMinutes = 10,
            ScreenshotRetentionDays = 30,
        };
        await database.TrackingSettings.SaveAsync(updated);

        Assert.Equal(updated, await database.TrackingSettings.GetAsync());
    }

    [Fact]
    public async Task AdjacentMatchingActivitiesAreMergedAndReturnedWithSession()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var started = StudySession.Start("整理错题", StartTime);
        await database.Sessions.AddAsync(started);

        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            started.Session.Id,
            StartTime,
            StartTime.AddSeconds(5),
            "Code",
            "TimeBet - Visual Studio Code"));
        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            started.Session.Id,
            StartTime.AddSeconds(5),
            StartTime.AddSeconds(10),
            "Code",
            "TimeBet - Visual Studio Code"));
        await database.Timeline.AppendOrMergeActivityAsync(ActivitySegment.Create(
            started.Session.Id,
            StartTime.AddSeconds(10),
            StartTime.AddSeconds(15),
            "Code",
            "TimeBet - Visual Studio Code",
            isIdle: true));

        var sessions = await database.Timeline.GetSessionsAsync(
            StartTime.AddDays(1),
            StartTime.AddDays(-1));

        var session = Assert.Single(sessions);
        Assert.Equal(2, session.Activities.Count);
        Assert.Equal(10, (session.Activities[0].EndedAtUtc - session.Activities[0].StartedAtUtc).TotalSeconds);
        Assert.True(session.Activities[1].IsIdle);
    }

    [Fact]
    public async Task DatabaseAllowsOnlyOneActiveSession()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();

        await database.Sessions.AddAsync(StudySession.Start("数学", StartTime));

        await Assert.ThrowsAsync<SqliteException>(() =>
            database.Sessions.AddAsync(StudySession.Start("英语", StartTime.AddMinutes(1))));
    }

    [Fact]
    public async Task PauseAndResumeTransitionsArePersisted()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var started = StudySession.Start("物理", StartTime);
        await database.Sessions.AddAsync(started);

        var paused = started.Session.Pause(StartTime.AddMinutes(4));
        await database.Sessions.SaveTransitionAsync(paused);
        var persistedPause = await database.Sessions.GetActiveAsync();
        Assert.NotNull(persistedPause);
        Assert.Equal(StudySessionStatus.Paused, persistedPause.Status);

        var resumed = paused.Session.Resume(StartTime.AddMinutes(9));
        await database.Sessions.SaveTransitionAsync(resumed);
        var persistedResume = await database.Sessions.GetActiveAsync();
        Assert.NotNull(persistedResume);
        Assert.Equal(StudySessionStatus.Running, persistedResume.Status);
        Assert.Equal(5 * 60, persistedResume.AccumulatedPausedSeconds);
    }

    [Fact]
    public async Task CompletingASessionCreditsTheLedgerAtomically()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var started = StudySession.Start("数学极限", StartTime);
        await database.Sessions.AddAsync(started);

        var completion = started.Session.Complete(
            StartTime.AddMinutes(25),
            "完成极限例题",
            [new SessionTaskDraft("完成 10 道题", true)]);
        await database.Sessions.CompleteAndCreditAsync(completion);

        Assert.Null(await database.Sessions.GetActiveAsync());
        Assert.Equal(25 * 60, await database.Ledger.GetBalanceSecondsAsync());

        await using var connection = await database.Factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM session_tasks WHERE session_id = $id;";
        command.Parameters.AddWithValue("$id", completion.Session.Id.ToString("D"));
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CompletionRollsBackWhenCreditInsertFails()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var started = StudySession.Start("英语阅读", StartTime);
        await database.Sessions.AddAsync(started);

        var completion = started.Session.Complete(
            StartTime.AddMinutes(10),
            "完成一篇阅读",
            []);
        var conflictingEntry = LedgerEntry.CreateAdjustment(
            60,
            "预置冲突",
            StartTime,
            completion.Credit.Id);
        await database.Ledger.AppendAsync(conflictingEntry);

        await Assert.ThrowsAsync<SqliteException>(() =>
            database.Sessions.CompleteAndCreditAsync(completion));

        var active = await database.Sessions.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(StudySessionStatus.Running, active.Status);
        Assert.Equal(60, await database.Ledger.GetBalanceSecondsAsync());
    }

    [Fact]
    public async Task LedgerEntriesCannotBeUpdatedOrDeleted()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var entry = LedgerEntry.CreateAdjustment(120, "测试入账", StartTime);
        await database.Ledger.AppendAsync(entry);

        await using var connection = await database.Factory.OpenAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE ledger_entries SET note = 'changed' WHERE id = $id;";
        update.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        await Assert.ThrowsAsync<SqliteException>(() => update.ExecuteNonQueryAsync());

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ledger_entries WHERE id = $id;";
        delete.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        await Assert.ThrowsAsync<SqliteException>(() => delete.ExecuteNonQueryAsync());
    }
}
