using System.Globalization;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Challenges;
using TimeBet.Application.Sessions;
using TimeBet.Domain;
using TimeBet.Domain.Ledger;

namespace TimeBet.Infrastructure.Tests;

public sealed class ChallengeServiceTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChallengeDefaultsToDisabledAndPublishesTransparentRules()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var service = CreateService(database, 0.60);

        var overview = await service.GetOverviewAsync();

        Assert.False(overview.Enabled);
        Assert.Equal(3, overview.DailyRoundLimit);
        Assert.Equal("star-wish-v1", overview.ProbabilityVersion);
        Assert.Equal(5, overview.ProbabilityTiers.Count);
        Assert.Equal(1m, overview.ProbabilityTiers.Sum(tier => tier.Probability));
    }

    [Fact]
    public async Task PlayAtomicallyPersistsRoundAndOneNetLedgerEntry()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));
        var service = CreateService(database, 0.60);
        await service.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));
        var roundId = Guid.NewGuid();

        var result = await service.PlayAsync(new PlayChallengeRequest(roundId, 300));

        Assert.Equal("small-fortune", result.Round.OutcomeCode);
        Assert.Equal(450, result.Round.PayoutSeconds);
        Assert.Equal(150, result.Round.NetSeconds);
        Assert.Equal(3750, result.Overview.BalanceSeconds);
        Assert.Equal(1, result.Overview.RoundsPlayedToday);
        Assert.Equal(3750, await database.Ledger.GetBalanceSecondsAsync());

        await using var connection = await database.Factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), MIN(l.amount_seconds), MIN(l.entry_type)
            FROM game_rounds AS r
            INNER JOIN ledger_entries AS l ON l.id = r.ledger_entry_id
            WHERE r.id = $id;
            """;
        command.Parameters.AddWithValue("$id", roundId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(150, reader.GetInt64(1));
        Assert.Equal("game-win", reader.GetString(2));
    }

    [Fact]
    public async Task RepeatingRoundIdReturnsCommittedResultWithoutSecondLedgerEntry()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));
        var firstService = CreateService(database, 0.60);
        await firstService.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));
        var roundId = Guid.NewGuid();
        var first = await firstService.PlayAsync(new PlayChallengeRequest(roundId, 300));
        var retryService = CreateService(database, 0.01);

        var retry = await retryService.PlayAsync(new PlayChallengeRequest(roundId, 300));

        Assert.Equal(first.Round, retry.Round);
        Assert.Equal(3750, await database.Ledger.GetBalanceSecondsAsync());
        await using var connection = await database.Factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM game_rounds;";
        Assert.Equal(1L, Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ChallengeRejectsDisabledActiveStudyAndWagerAboveLimit()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(3600, "测试余额", StartTime));
        var service = CreateService(database, 0.60);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 300)));

        await service.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 420)));

        var sessionService = new StudySessionService(
            database.Sessions,
            database.Ledger,
            new FixedClock(StartTime));
        await sessionService.StartAsync(new StartSessionRequest("正在学习"));
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 300)));
        Assert.Equal(3600, await database.Ledger.GetBalanceSecondsAsync());
    }

    [Fact]
    public async Task DailyLimitAllowsOnlyThreeCommittedRounds()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(36000, "测试余额", StartTime));
        var service = CreateService(database, 0.60);
        await service.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));

        for (var index = 0; index < 3; index++)
        {
            await service.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 60));
        }

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 60)));
        var overview = await service.GetOverviewAsync();
        Assert.Equal(3, overview.RoundsPlayedToday);
        Assert.Equal(0, overview.RoundsRemainingToday);
    }

    [Fact]
    public async Task DailyLimitResetsAtConfiguredLocalMidnight()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(36000, "测试余额", StartTime));
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "UTC+08-test",
            TimeSpan.FromHours(8),
            "UTC+08-test",
            "UTC+08-test");
        var beforeMidnight = new DateTimeOffset(2026, 8, 12, 15, 59, 0, TimeSpan.Zero);
        var beforeService = new ChallengeService(
            database.Challenges,
            new FixedClock(beforeMidnight),
            new FixedRandom(0.60),
            timeZone);
        await beforeService.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));
        await beforeService.PlayAsync(new PlayChallengeRequest(Guid.NewGuid(), 60));

        var afterService = new ChallengeService(
            database.Challenges,
            new FixedClock(beforeMidnight.AddMinutes(2)),
            new FixedRandom(0.60),
            timeZone);
        var overview = await afterService.GetOverviewAsync();

        Assert.Equal(0, overview.RoundsPlayedToday);
        Assert.Equal(3, overview.RoundsRemainingToday);
        Assert.Single(overview.RecentRounds);
    }

    [Fact]
    public async Task ConcurrentRequestsRecheckBalanceInsideWriteTransaction()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.Ledger.AppendAsync(LedgerEntry.CreateAdjustment(600, "测试余额", StartTime));
        var service = CreateService(database, 0.01);
        await service.UpdateSettingsAsync(new UpdateChallengeSettingsRequest(true));

        var results = await Task.WhenAll(
            TryPlayAsync(service, Guid.NewGuid(), 60),
            TryPlayAsync(service, Guid.NewGuid(), 60));

        Assert.Single(results, result => result);
        Assert.Equal(540, await database.Ledger.GetBalanceSecondsAsync());
    }

    private static ChallengeService CreateService(TemporaryDatabase database, double outcome)
    {
        return new ChallengeService(
            database.Challenges,
            new FixedClock(StartTime),
            new FixedRandom(outcome),
            TimeZoneInfo.Utc);
    }

    private static async Task<bool> TryPlayAsync(
        ChallengeService service,
        Guid roundId,
        long wagerSeconds)
    {
        try
        {
            await service.PlayAsync(new PlayChallengeRequest(roundId, wagerSeconds));
            return true;
        }
        catch (DomainRuleException)
        {
            return false;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedRandom(double outcome) : IRandomSource
    {
        public double NextUnitInterval() => outcome;
    }
}
