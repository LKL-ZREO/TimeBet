using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Sessions;

namespace TimeBet.Application.Sessions;

public sealed class StudySessionService
{
    private readonly IStudySessionRepository sessions;
    private readonly ILedgerRepository ledger;
    private readonly IClock clock;
    private readonly IPlanningTaskRepository? planningTasks;
    private readonly IPlanningTimeBlockRepository? planningTimeBlocks;
    private readonly ILeisureSessionRepository? leisureSessions;

    public StudySessionService(
        IStudySessionRepository sessions,
        ILedgerRepository ledger,
        IClock clock,
        IPlanningTaskRepository? planningTasks = null,
        IPlanningTimeBlockRepository? planningTimeBlocks = null,
        ILeisureSessionRepository? leisureSessions = null)
    {
        this.sessions = sessions;
        this.ledger = ledger;
        this.clock = clock;
        this.planningTasks = planningTasks;
        this.planningTimeBlocks = planningTimeBlocks;
        this.leisureSessions = leisureSessions;
    }

    public event EventHandler<StudyStateSnapshot>? StateChanged;

    public async Task<StudyStateSnapshot> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var session = await sessions.GetActiveAsync(cancellationToken);
        var balance = await ledger.GetBalanceSecondsAsync(cancellationToken);
        return ToState(session, balance);
    }

    public async Task<StudyStateSnapshot> StartAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ArgumentException("请填写本次学习目标。", nameof(request));
        }

        if (request.TaskId.HasValue)
        {
            if (planningTasks is null)
            {
                throw new InvalidOperationException("Planning tasks are unavailable.");
            }

            var task = await planningTasks.GetAsync(request.TaskId.Value, cancellationToken);
            if (task is null || task.Status != Domain.Tasks.PlanningTaskStatus.Active)
            {
                throw new DomainRuleException("Only active tasks can start a study session.");
            }
        }

        if (request.TimeBlockId.HasValue)
        {
            if (!request.TaskId.HasValue || planningTimeBlocks is null)
            {
                throw new DomainRuleException("A time block must be linked to its task.");
            }

            var timeBlock = await planningTimeBlocks.GetAsync(request.TimeBlockId.Value, cancellationToken);
            if (timeBlock is null ||
                timeBlock.TaskId != request.TaskId.Value ||
                timeBlock.Status != Domain.Tasks.PlanningTimeBlockStatus.Planned)
            {
                throw new DomainRuleException("Only a planned time block can start its linked task.");
            }
        }

        var active = await sessions.GetActiveAsync(cancellationToken);
        if (active is not null)
        {
            throw new DomainRuleException("A study session is already active.");
        }

        if (leisureSessions is not null &&
            await leisureSessions.GetActiveAsync(cancellationToken) is not null)
        {
            throw new DomainRuleException("请先结束当前自由时间，再开始学习计时。");
        }

        var transition = StudySession.Start(
            request.Goal,
            clock.UtcNow,
            request.CaptureScreenshots,
            taskId: request.TaskId,
            timeBlockId: request.TimeBlockId);
        await sessions.AddAsync(transition, cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        Publish(state);
        return state;
    }

    public async Task<StudyStateSnapshot> PauseAsync(CancellationToken cancellationToken = default)
    {
        var active = await RequireActiveAsync(cancellationToken);
        var transition = active.Pause(clock.UtcNow);
        await sessions.SaveTransitionAsync(transition, cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        Publish(state);
        return state;
    }

    public async Task<StudyStateSnapshot> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var active = await RequireActiveAsync(cancellationToken);
        var transition = active.Resume(clock.UtcNow);
        await sessions.SaveTransitionAsync(transition, cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        Publish(state);
        return state;
    }

    public async Task<FinishSessionResult> FinishAsync(
        FinishSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CompletionNote))
        {
            throw new ArgumentException("请填写本次完成内容或未完成原因。", nameof(request));
        }

        var taskInputs = request.Tasks ?? [];
        var active = await RequireActiveAsync(cancellationToken);
        var completion = active.Complete(
            clock.UtcNow,
            request.CompletionNote,
            taskInputs
                .Where(task => task is not null && !string.IsNullOrWhiteSpace(task.Content))
                .Select(task => new SessionTaskDraft(task.Content, task.IsCompleted)));

        await sessions.CompleteAndCreditAsync(completion, cancellationToken);
        var balance = await ledger.GetBalanceSecondsAsync(cancellationToken);
        var completedSession = ToSessionSnapshot(completion.Session, completion.Session.EndedAtUtc!.Value);
        var state = new StudyStateSnapshot(null, balance, clock.UtcNow);
        Publish(state);
        return new FinishSessionResult(state, completedSession);
    }

    private async Task<StudySession> RequireActiveAsync(CancellationToken cancellationToken)
    {
        return await sessions.GetActiveAsync(cancellationToken)
            ?? throw new DomainRuleException("There is no active study session.");
    }

    private StudyStateSnapshot ToState(StudySession? session, long balanceSeconds)
    {
        var asOf = clock.UtcNow;
        return new StudyStateSnapshot(
            session is null ? null : ToSessionSnapshot(session, asOf),
            balanceSeconds,
            asOf);
    }

    private static StudySessionSnapshot ToSessionSnapshot(StudySession session, DateTimeOffset asOfUtc)
    {
        return new StudySessionSnapshot(
            session.Id,
            session.Goal,
            session.TaskId,
            session.TimeBlockId,
            session.CaptureScreenshots,
            session.Status.ToString().ToLowerInvariant(),
            session.StartedAtUtc,
            session.LastTransitionAtUtc,
            session.PausedAtUtc,
            session.EndedAtUtc,
            session.GetEffectiveDurationSeconds(asOfUtc),
            session.CompletionNote);
    }

    private void Publish(StudyStateSnapshot state)
    {
        StateChanged?.Invoke(this, state);
    }
}
