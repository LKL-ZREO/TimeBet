using TimeBet.Domain;

namespace TimeBet.Domain.Rewards;

public sealed record Reward(
    Guid Id,
    string Name,
    string? Description,
    long CostSeconds,
    string? ImagePath,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static Reward Create(
        string name,
        long costSeconds,
        DateTimeOffset createdAtUtc,
        string? description = null,
        string? imagePath = null,
        Guid? id = null)
    {
        var created = DomainGuards.Utc(createdAtUtc, nameof(createdAtUtc));
        var rewardId = id ?? Guid.NewGuid();
        if (rewardId == Guid.Empty)
        {
            throw new ArgumentException("Reward ID cannot be empty.", nameof(id));
        }

        return new Reward(
            rewardId,
            DomainGuards.NonEmpty(name, nameof(name)),
            description?.Trim(),
            DomainGuards.Positive(costSeconds, nameof(costSeconds)),
            imagePath?.Trim(),
            true,
            created,
            created);
    }

    public Reward Edit(
        string name,
        long costSeconds,
        DateTimeOffset updatedAtUtc,
        string? description = null)
    {
        var updated = ValidateUpdateTime(updatedAtUtc);
        return this with
        {
            Name = DomainGuards.NonEmpty(name, nameof(name)),
            Description = NormalizeOptional(description),
            CostSeconds = DomainGuards.Positive(costSeconds, nameof(costSeconds)),
            UpdatedAtUtc = updated,
        };
    }

    public Reward SetActive(bool isActive, DateTimeOffset updatedAtUtc)
    {
        return this with
        {
            IsActive = isActive,
            UpdatedAtUtc = ValidateUpdateTime(updatedAtUtc),
        };
    }

    private DateTimeOffset ValidateUpdateTime(DateTimeOffset updatedAtUtc)
    {
        var updated = DomainGuards.Utc(updatedAtUtc, nameof(updatedAtUtc));
        if (updated < UpdatedAtUtc)
        {
            throw new DomainRuleException("Reward updates must be chronological.");
        }

        return updated;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
