using TimeBet.Domain;

namespace TimeBet.Domain.Sessions;

public sealed record SessionEvent(
    Guid Id,
    Guid SessionId,
    int Sequence,
    SessionEventType Type,
    DateTimeOffset OccurredAtUtc)
{
    public static SessionEvent Create(
        Guid sessionId,
        int sequence,
        SessionEventType type,
        DateTimeOffset occurredAtUtc,
        Guid? id = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be positive.");
        }

        return new SessionEvent(
            id ?? Guid.NewGuid(),
            sessionId,
            sequence,
            type,
            DomainGuards.Utc(occurredAtUtc, nameof(occurredAtUtc)));
    }
}
