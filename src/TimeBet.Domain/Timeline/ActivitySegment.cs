using TimeBet.Domain;

namespace TimeBet.Domain.Timeline;

public sealed record ActivitySegment(
    Guid Id,
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string ProcessName,
    string WindowTitle,
    bool IsIdle,
    string? DocumentName,
    string? DocumentType)
{
    public static ActivitySegment Create(
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        string processName,
        string windowTitle,
        bool isIdle = false,
        string? documentName = null,
        string? documentType = null,
        Guid? id = null)
    {
        var started = DomainGuards.Utc(startedAtUtc, nameof(startedAtUtc));
        var ended = DomainGuards.Utc(endedAtUtc, nameof(endedAtUtc));
        if (sessionId == Guid.Empty || ended <= started)
        {
            throw new DomainRuleException("Activity segment IDs and timestamps are invalid.");
        }

        return new ActivitySegment(
            id ?? Guid.NewGuid(),
            sessionId,
            started,
            ended,
            DomainGuards.NonEmpty(processName, nameof(processName)),
            DomainGuards.NonEmpty(windowTitle, nameof(windowTitle)),
            isIdle,
            documentName?.Trim(),
            documentType?.Trim());
    }
}
