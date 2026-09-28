using TimeBet.Domain.Ledger;

namespace TimeBet.Domain.Sessions;

public sealed record StudySessionCompletion(
    StudySession Session,
    SessionEvent Event,
    IReadOnlyList<SessionTask> Tasks,
    LedgerEntry Credit);
