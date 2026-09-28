using TimeBet.Domain;

namespace TimeBet.Domain.Sessions;

public sealed record SessionTask(
    Guid Id,
    Guid SessionId,
    string Content,
    bool IsCompleted,
    int SortOrder)
{
    public static SessionTask Create(
        Guid sessionId,
        string content,
        bool isCompleted,
        int sortOrder,
        Guid? id = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), sortOrder, "Sort order cannot be negative.");
        }

        return new SessionTask(
            id ?? Guid.NewGuid(),
            sessionId,
            DomainGuards.NonEmpty(content, nameof(content)),
            isCompleted,
            sortOrder);
    }
}

public sealed record SessionTaskDraft(string Content, bool IsCompleted);
