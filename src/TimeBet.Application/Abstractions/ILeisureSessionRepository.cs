using TimeBet.Domain.Leisure;

namespace TimeBet.Application.Abstractions;

public sealed record LeisureSettlementResult(LeisureSession Session, long BalanceSeconds);

public interface ILeisureSessionRepository
{
    Task AddAsync(LeisureSession session, CancellationToken cancellationToken = default);

    Task<LeisureSession?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LeisureSession session, CancellationToken cancellationToken = default);

    Task<LeisureSettlementResult> CompleteAndDebitAsync(
        LeisureSession completed,
        CancellationToken cancellationToken = default);
}
