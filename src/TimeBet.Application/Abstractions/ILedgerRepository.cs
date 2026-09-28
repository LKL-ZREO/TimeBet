using TimeBet.Domain.Ledger;

namespace TimeBet.Application.Abstractions;

public interface ILedgerRepository
{
    Task AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default);

    Task<long> GetBalanceSecondsAsync(CancellationToken cancellationToken = default);
}
