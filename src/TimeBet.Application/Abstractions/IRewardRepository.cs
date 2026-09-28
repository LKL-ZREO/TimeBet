using TimeBet.Domain.Rewards;

namespace TimeBet.Application.Abstractions;

public sealed record RewardRedemptionCommit(
    RewardRedemption Redemption,
    long BalanceSeconds);

public interface IRewardRepository
{
    Task<IReadOnlyList<Reward>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Reward?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Reward reward, CancellationToken cancellationToken = default);

    Task UpdateAsync(Reward reward, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RewardRedemption>> GetRecentRedemptionsAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<RewardRedemptionCommit> RedeemAsync(
        Guid rewardId,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken = default);
}
