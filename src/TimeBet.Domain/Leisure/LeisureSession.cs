namespace TimeBet.Domain.Leisure;

public sealed record LeisureSession(
    Guid Id,
    string Activity,
    int PlannedSeconds,
    LeisureSessionStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastTransitionAtUtc,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long AccumulatedPausedSeconds,
    long? EffectiveDurationSeconds,
    long ChargedSeconds,
    string? CompletionNote)
{
    public static LeisureSession Start(
        string activity,
        int plannedSeconds,
        DateTimeOffset startedAtUtc,
        Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedSeconds);

        var sessionId = id ?? Guid.NewGuid();
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Leisure session ID cannot be empty.", nameof(id));
        }

        var started = DomainGuards.Utc(startedAtUtc, nameof(startedAtUtc));
        return new LeisureSession(
            sessionId,
            DomainGuards.NonEmpty(activity, nameof(activity)),
            plannedSeconds,
            LeisureSessionStatus.Running,
            started,
            started,
            null,
            null,
            0,
            null,
            0,
            null);
    }

    public static LeisureSession Restore(
        Guid id,
        string activity,
        int plannedSeconds,
        LeisureSessionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastTransitionAtUtc,
        DateTimeOffset? pausedAtUtc,
        DateTimeOffset? endedAtUtc,
        long accumulatedPausedSeconds,
        long? effectiveDurationSeconds,
        long chargedSeconds,
        string? completionNote)
    {
        if (id == Guid.Empty || plannedSeconds <= 0 || accumulatedPausedSeconds < 0 || chargedSeconds < 0)
        {
            throw new DomainRuleException("Leisure session data is invalid.");
        }

        var started = DomainGuards.Utc(startedAtUtc, nameof(startedAtUtc));
        var lastTransition = DomainGuards.Utc(lastTransitionAtUtc, nameof(lastTransitionAtUtc));
        DateTimeOffset? paused = pausedAtUtc.HasValue
            ? DomainGuards.Utc(pausedAtUtc.Value, nameof(pausedAtUtc))
            : null;
        DateTimeOffset? ended = endedAtUtc.HasValue
            ? DomainGuards.Utc(endedAtUtc.Value, nameof(endedAtUtc))
            : null;

        if (lastTransition < started || paused < started || ended < started)
        {
            throw new DomainRuleException("Leisure session timestamps are invalid.");
        }

        if (status == LeisureSessionStatus.Completed && (!ended.HasValue || !effectiveDurationSeconds.HasValue))
        {
            throw new DomainRuleException("Completed leisure sessions require settlement data.");
        }

        if (status != LeisureSessionStatus.Completed && (ended.HasValue || effectiveDurationSeconds.HasValue || chargedSeconds != 0))
        {
            throw new DomainRuleException("Active leisure sessions cannot contain settlement data.");
        }

        if (status == LeisureSessionStatus.Paused && !paused.HasValue ||
            status != LeisureSessionStatus.Paused && paused.HasValue)
        {
            throw new DomainRuleException("Leisure session pause state is invalid.");
        }

        if (effectiveDurationSeconds.HasValue && chargedSeconds > effectiveDurationSeconds.Value)
        {
            throw new DomainRuleException("Charged leisure time cannot exceed actual time.");
        }

        return new LeisureSession(
            id,
            DomainGuards.NonEmpty(activity, nameof(activity)),
            plannedSeconds,
            status,
            started,
            lastTransition,
            paused,
            ended,
            accumulatedPausedSeconds,
            effectiveDurationSeconds,
            chargedSeconds,
            completionNote);
    }

    public LeisureSession Pause(DateTimeOffset pausedAtUtc)
    {
        var pausedAt = DomainGuards.Utc(pausedAtUtc, nameof(pausedAtUtc));
        EnsureTransition(LeisureSessionStatus.Running, pausedAt);
        return this with
        {
            Status = LeisureSessionStatus.Paused,
            LastTransitionAtUtc = pausedAt,
            PausedAtUtc = pausedAt,
        };
    }

    public LeisureSession Resume(DateTimeOffset resumedAtUtc)
    {
        var resumedAt = DomainGuards.Utc(resumedAtUtc, nameof(resumedAtUtc));
        EnsureTransition(LeisureSessionStatus.Paused, resumedAt);
        return this with
        {
            Status = LeisureSessionStatus.Running,
            LastTransitionAtUtc = resumedAt,
            PausedAtUtc = null,
            AccumulatedPausedSeconds = AccumulatedPausedSeconds + SecondsBetween(PausedAtUtc!.Value, resumedAt),
        };
    }

    public LeisureSession Complete(DateTimeOffset completedAtUtc, string? completionNote)
    {
        var completedAt = DomainGuards.Utc(completedAtUtc, nameof(completedAtUtc));
        if (Status is not (LeisureSessionStatus.Running or LeisureSessionStatus.Paused) || completedAt < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Only an active leisure session can be completed.");
        }

        var pausedSeconds = AccumulatedPausedSeconds;
        if (Status == LeisureSessionStatus.Paused)
        {
            pausedSeconds += SecondsBetween(PausedAtUtc!.Value, completedAt);
        }

        var effectiveSeconds = SecondsBetween(StartedAtUtc, completedAt) - pausedSeconds;
        if (effectiveSeconds <= 0)
        {
            throw new DomainRuleException("A leisure session must contain at least one effective second.");
        }

        return this with
        {
            Status = LeisureSessionStatus.Completed,
            LastTransitionAtUtc = completedAt,
            PausedAtUtc = null,
            EndedAtUtc = completedAt,
            AccumulatedPausedSeconds = pausedSeconds,
            EffectiveDurationSeconds = effectiveSeconds,
            CompletionNote = string.IsNullOrWhiteSpace(completionNote) ? null : completionNote.Trim(),
        };
    }

    public LeisureSession SetChargedSeconds(long chargedSeconds)
    {
        if (Status != LeisureSessionStatus.Completed || !EffectiveDurationSeconds.HasValue ||
            chargedSeconds < 0 || chargedSeconds > EffectiveDurationSeconds.Value)
        {
            throw new DomainRuleException("Leisure settlement amount is invalid.");
        }

        return this with { ChargedSeconds = chargedSeconds };
    }

    public long GetEffectiveDurationSeconds(DateTimeOffset nowUtc)
    {
        if (Status == LeisureSessionStatus.Completed)
        {
            return EffectiveDurationSeconds!.Value;
        }

        var now = DomainGuards.Utc(nowUtc, nameof(nowUtc));
        if (now < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Current time cannot precede the last leisure transition.");
        }

        var pausedSeconds = AccumulatedPausedSeconds;
        if (Status == LeisureSessionStatus.Paused)
        {
            pausedSeconds += SecondsBetween(PausedAtUtc!.Value, now);
        }

        return Math.Max(0, SecondsBetween(StartedAtUtc, now) - pausedSeconds);
    }

    private void EnsureTransition(LeisureSessionStatus expected, DateTimeOffset atUtc)
    {
        if (Status != expected || atUtc < LastTransitionAtUtc)
        {
            throw new DomainRuleException("Leisure session state has changed.");
        }
    }

    private static long SecondsBetween(DateTimeOffset start, DateTimeOffset end) =>
        (long)Math.Floor((end - start).TotalSeconds);
}
