using TimeBet.Domain.Ledger;

namespace TimeBet.Domain.Sessions;

public sealed class StudySession
{
    private StudySession(
        Guid id,
        string goal,
        Guid? taskId,
        Guid? timeBlockId,
        bool captureScreenshots,
        StudySessionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastTransitionAtUtc,
        DateTimeOffset? pausedAtUtc,
        DateTimeOffset? endedAtUtc,
        long accumulatedPausedSeconds,
        long? effectiveDurationSeconds,
        string? completionNote,
        int nextEventSequence)
    {
        Id = id;
        Goal = goal;
        TaskId = taskId;
        TimeBlockId = timeBlockId;
        CaptureScreenshots = captureScreenshots;
        Status = status;
        StartedAtUtc = startedAtUtc;
        LastTransitionAtUtc = lastTransitionAtUtc;
        PausedAtUtc = pausedAtUtc;
        EndedAtUtc = endedAtUtc;
        AccumulatedPausedSeconds = accumulatedPausedSeconds;
        EffectiveDurationSeconds = effectiveDurationSeconds;
        CompletionNote = completionNote;
        NextEventSequence = nextEventSequence;
    }

    public Guid Id { get; }

    public string Goal { get; }

    public Guid? TaskId { get; }

    public Guid? TimeBlockId { get; }

    public bool CaptureScreenshots { get; }

    public StudySessionStatus Status { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset LastTransitionAtUtc { get; }

    public DateTimeOffset? PausedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public long AccumulatedPausedSeconds { get; }

    public long? EffectiveDurationSeconds { get; }

    public string? CompletionNote { get; }

    public int NextEventSequence { get; }

    public static SessionTransition Start(
        string goal,
        DateTimeOffset startedAtUtc,
        bool captureScreenshots = false,
        Guid? id = null,
        Guid? taskId = null,
        Guid? timeBlockId = null)
    {
        var started = DomainGuards.Utc(startedAtUtc, nameof(startedAtUtc));
        var sessionId = id ?? Guid.NewGuid();
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(id));
        }

        var session = new StudySession(
            sessionId,
            DomainGuards.NonEmpty(goal, nameof(goal)),
            taskId,
            timeBlockId,
            captureScreenshots,
            StudySessionStatus.Running,
            started,
            started,
            null,
            null,
            0,
            null,
            null,
            2);

        return new SessionTransition(
            session,
            SessionEvent.Create(sessionId, 1, SessionEventType.Started, started));
    }

    public static StudySession Restore(
        Guid id,
        string goal,
        Guid? taskId,
        Guid? timeBlockId,
        bool captureScreenshots,
        StudySessionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastTransitionAtUtc,
        DateTimeOffset? pausedAtUtc,
        DateTimeOffset? endedAtUtc,
        long accumulatedPausedSeconds,
        long? effectiveDurationSeconds,
        string? completionNote,
        int nextEventSequence)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(id));
        }

        var started = DomainGuards.Utc(startedAtUtc, nameof(startedAtUtc));
        var lastTransition = DomainGuards.Utc(lastTransitionAtUtc, nameof(lastTransitionAtUtc));
        DateTimeOffset? paused = pausedAtUtc.HasValue
            ? DomainGuards.Utc(pausedAtUtc.Value, nameof(pausedAtUtc))
            : null;
        DateTimeOffset? ended = endedAtUtc.HasValue
            ? DomainGuards.Utc(endedAtUtc.Value, nameof(endedAtUtc))
            : null;

        if (lastTransition < started || (paused.HasValue && paused < started) || (ended.HasValue && ended < started))
        {
            throw new DomainRuleException("Session timestamps must be ordered.");
        }

        if (accumulatedPausedSeconds < 0 || nextEventSequence < 2)
        {
            throw new DomainRuleException("Session duration and event sequence are invalid.");
        }

        if (status == StudySessionStatus.Completed && (!ended.HasValue || !effectiveDurationSeconds.HasValue))
        {
            throw new DomainRuleException("Completed sessions require an end time and effective duration.");
        }

        if (status == StudySessionStatus.Completed && effectiveDurationSeconds.GetValueOrDefault() <= 0)
        {
            throw new DomainRuleException("Completed sessions require positive effective duration.");
        }

        if (status != StudySessionStatus.Completed && (ended.HasValue || effectiveDurationSeconds.HasValue))
        {
            throw new DomainRuleException("Active sessions cannot have completion data.");
        }

        if (status == StudySessionStatus.Paused && !paused.HasValue)
        {
            throw new DomainRuleException("Paused sessions require a pause timestamp.");
        }

        if (status != StudySessionStatus.Paused && paused.HasValue)
        {
            throw new DomainRuleException("Only paused sessions can have a pause timestamp.");
        }

        return new StudySession(
            id,
            DomainGuards.NonEmpty(goal, nameof(goal)),
            taskId,
            timeBlockId,
            captureScreenshots,
            status,
            started,
            lastTransition,
            paused,
            ended,
            accumulatedPausedSeconds,
            effectiveDurationSeconds,
            completionNote,
            nextEventSequence);
    }

    public SessionTransition Pause(DateTimeOffset pausedAtUtc)
    {
        var pausedAt = DomainGuards.Utc(pausedAtUtc, nameof(pausedAtUtc));
        EnsureTransitionAllowed(StudySessionStatus.Running, pausedAt);

        var session = Copy(
            status: StudySessionStatus.Paused,
            lastTransitionAtUtc: pausedAt,
            pausedAtUtc: pausedAt,
            nextEventSequence: NextEventSequence + 1);

        return new SessionTransition(
            session,
            SessionEvent.Create(Id, NextEventSequence, SessionEventType.Paused, pausedAt));
    }

    public SessionTransition Resume(DateTimeOffset resumedAtUtc)
    {
        var resumedAt = DomainGuards.Utc(resumedAtUtc, nameof(resumedAtUtc));
        EnsureTransitionAllowed(StudySessionStatus.Paused, resumedAt);

        var pausedSeconds = AccumulatedPausedSeconds + SecondsBetween(PausedAtUtc!.Value, resumedAt);
        var session = Copy(
            status: StudySessionStatus.Running,
            lastTransitionAtUtc: resumedAt,
            pausedAtUtc: null,
            clearPausedAtUtc: true,
            accumulatedPausedSeconds: pausedSeconds,
            nextEventSequence: NextEventSequence + 1);

        return new SessionTransition(
            session,
            SessionEvent.Create(Id, NextEventSequence, SessionEventType.Resumed, resumedAt));
    }

    public StudySessionCompletion Complete(
        DateTimeOffset completedAtUtc,
        string completionNote,
        IEnumerable<SessionTaskDraft> tasks)
    {
        var completedAt = DomainGuards.Utc(completedAtUtc, nameof(completedAtUtc));
        if (Status is not (StudySessionStatus.Running or StudySessionStatus.Paused))
        {
            throw new DomainRuleException("Only active sessions can be completed.");
        }

        if (completedAt < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Completion time cannot precede the last session transition.");
        }

        var finalPausedSeconds = AccumulatedPausedSeconds;
        if (Status == StudySessionStatus.Paused)
        {
            finalPausedSeconds += SecondsBetween(PausedAtUtc!.Value, completedAt);
        }

        var effectiveSeconds = SecondsBetween(StartedAtUtc, completedAt) - finalPausedSeconds;
        if (effectiveSeconds <= 0)
        {
            throw new DomainRuleException("A completed session must contain at least one effective second.");
        }

        var session = Copy(
            status: StudySessionStatus.Completed,
            lastTransitionAtUtc: completedAt,
            pausedAtUtc: null,
            clearPausedAtUtc: true,
            endedAtUtc: completedAt,
            accumulatedPausedSeconds: finalPausedSeconds,
            effectiveDurationSeconds: effectiveSeconds,
            completionNote: DomainGuards.NonEmpty(completionNote, nameof(completionNote)),
            nextEventSequence: NextEventSequence + 1);

        var taskList = tasks
            .Select((task, index) => SessionTask.Create(Id, task.Content, task.IsCompleted, index))
            .ToArray();

        return new StudySessionCompletion(
            session,
            SessionEvent.Create(Id, NextEventSequence, SessionEventType.Completed, completedAt),
            taskList,
            LedgerEntry.ForStudySession(session, completedAt));
    }

    public long GetEffectiveDurationSeconds(DateTimeOffset nowUtc)
    {
        var now = DomainGuards.Utc(nowUtc, nameof(nowUtc));
        if (Status == StudySessionStatus.Completed)
        {
            return EffectiveDurationSeconds!.Value;
        }

        if (now < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Current time cannot precede the last session transition.");
        }

        var pausedSeconds = AccumulatedPausedSeconds;
        if (Status == StudySessionStatus.Paused)
        {
            pausedSeconds += SecondsBetween(PausedAtUtc!.Value, now);
        }

        return Math.Max(0, SecondsBetween(StartedAtUtc, now) - pausedSeconds);
    }

    private StudySession Copy(
        StudySessionStatus? status = null,
        DateTimeOffset? lastTransitionAtUtc = null,
        DateTimeOffset? pausedAtUtc = null,
        bool clearPausedAtUtc = false,
        DateTimeOffset? endedAtUtc = null,
        long? accumulatedPausedSeconds = null,
        long? effectiveDurationSeconds = null,
        string? completionNote = null,
        int? nextEventSequence = null)
    {
        return new StudySession(
            Id,
            Goal,
            TaskId,
            TimeBlockId,
            CaptureScreenshots,
            status ?? Status,
            StartedAtUtc,
            lastTransitionAtUtc ?? LastTransitionAtUtc,
            clearPausedAtUtc ? null : pausedAtUtc ?? PausedAtUtc,
            endedAtUtc ?? EndedAtUtc,
            accumulatedPausedSeconds ?? AccumulatedPausedSeconds,
            effectiveDurationSeconds ?? EffectiveDurationSeconds,
            completionNote ?? CompletionNote,
            nextEventSequence ?? NextEventSequence);
    }

    private void EnsureTransitionAllowed(StudySessionStatus expectedStatus, DateTimeOffset atUtc)
    {
        if (Status != expectedStatus)
        {
            throw new DomainRuleException($"Session must be {expectedStatus} to perform this transition.");
        }

        if (atUtc < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Session transitions must be chronological.");
        }
    }

    private static long SecondsBetween(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        return (long)Math.Floor((endUtc - startUtc).TotalSeconds);
    }
}
