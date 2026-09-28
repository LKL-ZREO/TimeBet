namespace TimeBet.Domain.Sessions;

public sealed record SessionTransition(StudySession Session, SessionEvent Event);
