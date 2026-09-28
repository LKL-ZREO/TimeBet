using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Rewards;

namespace TimeBet.Application.Rewards;

public sealed class RewardService
{
    private readonly IRewardRepository rewards;
    private readonly ILedgerRepository ledger;
    private readonly IClock clock;

    public RewardService(
        IRewardRepository rewards,
        ILedgerRepository ledger,
        IClock clock)
    {
        this.rewards = rewards;
        this.ledger = ledger;
        this.clock = clock;
    }

    public async Task<RewardCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var allRewards = await rewards.GetAllAsync(cancellationToken);
        var recent = await rewards.GetRecentRedemptionsAsync(8, cancellationToken);
        var balance = await ledger.GetBalanceSecondsAsync(cancellationToken);
        return ToCatalog(balance, allRewards, recent);
    }

    public async Task<RewardCatalogSnapshot> CreateAsync(
        CreateRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = ValidateName(request.Name);
        var reward = Reward.Create(
            name,
            request.CostSeconds,
            clock.UtcNow,
            request.Description);
        await rewards.AddAsync(reward, cancellationToken);
        return await GetCatalogAsync(cancellationToken);
    }

    public async Task<RewardCatalogSnapshot> UpdateAsync(
        UpdateRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(request.Id);
        var name = ValidateName(request.Name);
        var current = await rewards.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("奖励不存在或已被删除。");
        var updated = current.Edit(
            name,
            request.CostSeconds,
            clock.UtcNow,
            request.Description);
        await rewards.UpdateAsync(updated, cancellationToken);
        return await GetCatalogAsync(cancellationToken);
    }

    public async Task<RewardCatalogSnapshot> SetActiveAsync(
        SetRewardActiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(request.Id);
        var current = await rewards.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("奖励不存在或已被删除。");
        await rewards.UpdateAsync(current.SetActive(request.IsActive, clock.UtcNow), cancellationToken);
        return await GetCatalogAsync(cancellationToken);
    }

    public async Task<RedeemRewardResult> RedeemAsync(
        RedeemRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(request.Id);
        var commit = await rewards.RedeemAsync(request.Id, clock.UtcNow, cancellationToken);
        var allRewards = await rewards.GetAllAsync(cancellationToken);
        var recent = await rewards.GetRecentRedemptionsAsync(8, cancellationToken);
        return new RedeemRewardResult(
            ToCatalog(commit.BalanceSeconds, allRewards, recent),
            ToSnapshot(commit.Redemption));
    }

    private static RewardCatalogSnapshot ToCatalog(
        long balanceSeconds,
        IEnumerable<Reward> rewards,
        IEnumerable<RewardRedemption> redemptions)
    {
        return new RewardCatalogSnapshot(
            balanceSeconds,
            rewards.Select(ToSnapshot).ToArray(),
            redemptions.Select(ToSnapshot).ToArray());
    }

    private static RewardSnapshot ToSnapshot(Reward reward)
    {
        return new RewardSnapshot(
            reward.Id,
            reward.Name,
            reward.Description,
            reward.CostSeconds,
            reward.IsActive,
            reward.CreatedAtUtc,
            reward.UpdatedAtUtc);
    }

    private static RewardRedemptionSnapshot ToSnapshot(RewardRedemption redemption)
    {
        return new RewardRedemptionSnapshot(
            redemption.Id,
            redemption.RewardId,
            redemption.RewardNameSnapshot,
            redemption.CostSeconds,
            redemption.RedeemedAtUtc);
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("奖励 ID 无效。", nameof(id));
        }
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("请填写奖励名称。", nameof(name));
        }

        return name.Trim();
    }
}
