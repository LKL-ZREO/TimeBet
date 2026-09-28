using TimeBet.Application.Abstractions;
using TimeBet.Application.Sessions;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Sessions;

namespace TimeBet.Infrastructure.Tests;

public sealed class StudySessionServiceTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionWorkflowPublishesStateAndCreditsEffectiveTime()
    {
        var clock = new MutableClock(StartTime);
        var ledger = new MemoryLedgerRepository();
        var sessions = new MemorySessionRepository(ledger);
        var service = new StudySessionService(sessions, ledger, clock);
        var publishedStates = new List<StudyStateSnapshot>();
        service.StateChanged += (_, state) => publishedStates.Add(state);

        var started = await service.StartAsync(new StartSessionRequest("完成极限章节"));
        Assert.Equal("running", started.Session?.Status);

        clock.UtcNow = StartTime.AddMinutes(10);
        var paused = await service.PauseAsync();
        Assert.Equal("paused", paused.Session?.Status);

        clock.UtcNow = StartTime.AddMinutes(15);
        await service.ResumeAsync();

        clock.UtcNow = StartTime.AddMinutes(35);
        var finished = await service.FinishAsync(new FinishSessionRequest(
            "完成例题并整理错题",
            [new SessionTaskInput("完成 12 道题", true)]));

        Assert.Null(finished.State.Session);
        Assert.Equal(30 * 60, finished.CompletedSession.EffectiveDurationSeconds);
        Assert.Equal(30 * 60, finished.State.BalanceSeconds);
        Assert.Equal(4, publishedStates.Count);
        Assert.NotNull(sessions.Completion);
        Assert.Single(sessions.Completion.Tasks);
    }

    [Fact]
    public async Task FinishAcceptsMissingTaskListButRequiresCompletionNote()
    {
        var clock = new MutableClock(StartTime);
        var ledger = new MemoryLedgerRepository();
        var sessions = new MemorySessionRepository(ledger);
        var service = new StudySessionService(sessions, ledger, clock);
        await service.StartAsync(new StartSessionRequest("英语阅读"));
        clock.UtcNow = StartTime.AddMinutes(5);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FinishAsync(new FinishSessionRequest(" ", null)));

        var finished = await service.FinishAsync(new FinishSessionRequest("完成一篇阅读", null));
        Assert.Equal(300, finished.State.BalanceSeconds);
        Assert.Empty(sessions.Completion!.Tasks);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class MemoryLedgerRepository : ILedgerRepository
    {
        private readonly List<LedgerEntry> entries = [];

        public Task AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
        {
            entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<long> GetBalanceSecondsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entries.Sum(entry => entry.AmountSeconds));
        }
    }

    private sealed class MemorySessionRepository(MemoryLedgerRepository ledger) : IStudySessionRepository
    {
        public StudySession? Active { get; private set; }

        public StudySessionCompletion? Completion { get; private set; }

        public Task AddAsync(SessionTransition started, CancellationToken cancellationToken = default)
        {
            Active = started.Session;
            return Task.CompletedTask;
        }

        public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Active);
        }

        public Task SaveTransitionAsync(
            SessionTransition transition,
            CancellationToken cancellationToken = default)
        {
            Active = transition.Session;
            return Task.CompletedTask;
        }

        public async Task CompleteAndCreditAsync(
            StudySessionCompletion completion,
            CancellationToken cancellationToken = default)
        {
            Completion = completion;
            Active = null;
            await ledger.AppendAsync(completion.Credit, cancellationToken);
        }
    }
}
