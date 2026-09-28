using TimeBet.Domain.Sessions;

namespace TimeBet.Application.Abstractions;

public interface IStudySessionRepository
{
    Task AddAsync(SessionTransition started, CancellationToken cancellationToken = default);

    Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task SaveTransitionAsync(SessionTransition transition, CancellationToken cancellationToken = default);

    Task CompleteAndCreditAsync(
        StudySessionCompletion completion,
        CancellationToken cancellationToken = default);
}
