namespace TimeBet.Application.Rewards;

public sealed record CreateRewardRequest(
    string? Name,
    string? Description,
    long CostSeconds);

public sealed record UpdateRewardRequest(
    Guid Id,
    string? Name,
    string? Description,
    long CostSeconds);

public sealed record SetRewardActiveRequest(Guid Id, bool IsActive);

public sealed record RedeemRewardRequest(Guid Id);

public sealed record RewardSnapshot(
    Guid Id,
    string Name,
    string? Description,
    long CostSeconds,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RewardRedemptionSnapshot(
    Guid Id,
    Guid RewardId,
    string RewardName,
    long CostSeconds,
    DateTimeOffset RedeemedAtUtc);

public sealed record RewardCatalogSnapshot(
    long BalanceSeconds,
    IReadOnlyList<RewardSnapshot> Rewards,
    IReadOnlyList<RewardRedemptionSnapshot> RecentRedemptions);

public sealed record RedeemRewardResult(
    RewardCatalogSnapshot Catalog,
    RewardRedemptionSnapshot Redemption);
