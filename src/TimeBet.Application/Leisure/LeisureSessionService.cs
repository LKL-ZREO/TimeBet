using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Leisure;

namespace TimeBet.Application.Leisure;

public sealed class LeisureSessionService
{
    private readonly ILeisureSessionRepository leisureSessions;
    private readonly IStudySessionRepository studySessions;
    private readonly ILedgerRepository ledger;
    private readonly IClock clock;

    public LeisureSessionService(
        ILeisureSessionRepository leisureSessions,
        IStudySessionRepository studySessions,
        ILedgerRepository ledger,
        IClock clock)
    {
        this.leisureSessions = leisureSessions;
        this.studySessions = studySessions;
        this.ledger = ledger;
        this.clock = clock;
    }

    public async Task<LeisureStateSnapshot> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var session = await leisureSessions.GetActiveAsync(cancellationToken);
        var balance = await ledger.GetBalanceSecondsAsync(cancellationToken);
        return ToState(session, balance);
    }

    public async Task<LeisureStateSnapshot> StartAsync(
        StartLeisureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Activity))
        {
            throw new ArgumentException("请填写这次自由时间准备做什么。", nameof(request));
        }

        if (request.PlannedMinutes is <= 0 or > 1_440)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "自由时间预算必须在 1 分钟到 24 小时之间。");
        }

        if (await studySessions.GetActiveAsync(cancellationToken) is not null)
        {
            throw new DomainRuleException("请先结束或暂停当前学习计时，再开始自由时间。");
        }

        if (await leisureSessions.GetActiveAsync(cancellationToken) is not null)
        {
            throw new DomainRuleException("已经有一段自由时间正在进行。");
        }

        var balance = await ledger.GetBalanceSecondsAsync(cancellationToken);
        if (balance <= 0)
        {
            throw new DomainRuleException("当前没有可用时间余额。恢复性休息不需要计费，可以放心休息。");
        }

        var session = LeisureSession.Start(
            request.Activity,
            checked(request.PlannedMinutes * 60),
            clock.UtcNow);
        await leisureSessions.AddAsync(session, cancellationToken);
        return ToState(session, balance);
    }

    public async Task<LeisureStateSnapshot> PauseAsync(CancellationToken cancellationToken = default)
    {
        var session = await RequireActiveAsync(cancellationToken);
        var updated = session.Pause(clock.UtcNow);
        await leisureSessions.SaveAsync(updated, cancellationToken);
        return ToState(updated, await ledger.GetBalanceSecondsAsync(cancellationToken));
    }

    public async Task<LeisureStateSnapshot> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var session = await RequireActiveAsync(cancellationToken);
        var updated = session.Resume(clock.UtcNow);
        await leisureSessions.SaveAsync(updated, cancellationToken);
        return ToState(updated, await ledger.GetBalanceSecondsAsync(cancellationToken));
    }

    public async Task<FinishLeisureResult> FinishAsync(
        FinishLeisureRequest request,
        CancellationToken cancellationToken = default)
    {
        var active = await RequireActiveAsync(cancellationToken);
        var completed = active.Complete(clock.UtcNow, request.CompletionNote);
        var settlement = await leisureSessions.CompleteAndDebitAsync(completed, cancellationToken);
        return new FinishLeisureResult(
            ToState(null, settlement.BalanceSeconds),
            ToSnapshot(settlement.Session, settlement.Session.EndedAtUtc!.Value));
    }

    private async Task<LeisureSession> RequireActiveAsync(CancellationToken cancellationToken)
    {
        return await leisureSessions.GetActiveAsync(cancellationToken)
            ?? throw new DomainRuleException("当前没有正在进行的自由时间。");
    }

    private LeisureStateSnapshot ToState(LeisureSession? session, long balance)
    {
        var asOf = clock.UtcNow;
        return new LeisureStateSnapshot(
            session is null ? null : ToSnapshot(session, asOf),
            balance,
            asOf);
    }

    private static LeisureSessionSnapshot ToSnapshot(LeisureSession session, DateTimeOffset asOfUtc)
    {
        return new LeisureSessionSnapshot(
            session.Id,
            session.Activity,
            session.PlannedSeconds,
            session.Status.ToString().ToLowerInvariant(),
            session.StartedAtUtc,
            session.LastTransitionAtUtc,
            session.PausedAtUtc,
            session.EndedAtUtc,
            session.GetEffectiveDurationSeconds(asOfUtc),
            session.ChargedSeconds,
            session.CompletionNote);
    }
}
