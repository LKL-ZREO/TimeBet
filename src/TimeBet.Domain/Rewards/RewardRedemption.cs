using TimeBet.Domain;

namespace TimeBet.Domain.Rewards;

public sealed record RewardRedemption(
    Guid Id,
    Guid RewardId,
    Guid LedgerEntryId,
    string RewardNameSnapshot,
    long CostSeconds,
    DateTimeOffset RedeemedAtUtc)
{
    public static RewardRedemption Create(
        Guid rewardId,
        Guid ledgerEntryId,
        string rewardNameSnapshot,
        long costSeconds,
        DateTimeOffset redeemedAtUtc,
        Guid? id = null)
    {
        if (rewardId == Guid.Empty || ledgerEntryId == Guid.Empty)
        {
            throw new ArgumentException("Reward and ledger IDs cannot be empty.");
        }

        var redemptionId = id ?? Guid.NewGuid();
        if (redemptionId == Guid.Empty)
        {
            throw new ArgumentException("Redemption ID cannot be empty.", nameof(id));
        }

        return new RewardRedemption(
            redemptionId,
            rewardId,
            ledgerEntryId,
            DomainGuards.NonEmpty(rewardNameSnapshot, nameof(rewardNameSnapshot)),
            DomainGuards.Positive(costSeconds, nameof(costSeconds)),
            DomainGuards.Utc(redeemedAtUtc, nameof(redeemedAtUtc)));
    }
}
