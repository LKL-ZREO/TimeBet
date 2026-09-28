using TimeBet.Domain;

namespace TimeBet.Domain.Challenges;

public sealed record GameRound(
    Guid Id,
    long WagerSeconds,
    long PayoutSeconds,
    string ProbabilityVersion,
    decimal Outcome,
    Guid LedgerEntryId,
    DateTimeOffset PlayedAtUtc)
{
    public static GameRound Create(
        long wagerSeconds,
        long payoutSeconds,
        string probabilityVersion,
        decimal outcome,
        Guid ledgerEntryId,
        DateTimeOffset playedAtUtc,
        Guid? id = null)
    {
        if (id == Guid.Empty || ledgerEntryId == Guid.Empty || wagerSeconds <= 0 || payoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wagerSeconds), "Challenge amounts are invalid.");
        }

        if (outcome is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "Outcome must be in the [0, 1) interval.");
        }

        return new GameRound(
            id ?? Guid.NewGuid(),
            wagerSeconds,
            payoutSeconds,
            DomainGuards.NonEmpty(probabilityVersion, nameof(probabilityVersion)),
            outcome,
            ledgerEntryId,
            DomainGuards.Utc(playedAtUtc, nameof(playedAtUtc)));
    }
}
