using TimeBet.Domain;
using TimeBet.Domain.Sessions;

namespace TimeBet.Domain.Ledger;

public sealed record LedgerEntry(
    Guid Id,
    LedgerEntryType Type,
    long AmountSeconds,
    string SourceType,
    string SourceId,
    string? Note,
    DateTimeOffset OccurredAtUtc)
{
    public static LedgerEntry ForStudySession(
        StudySession session,
        DateTimeOffset occurredAtUtc,
        Guid? id = null)
    {
        if (session.Status != StudySessionStatus.Completed || !session.EffectiveDurationSeconds.HasValue)
        {
            throw new DomainRuleException("Only completed sessions can create an earning entry.");
        }

        return Create(
            id ?? Guid.NewGuid(),
            LedgerEntryType.Earn,
            session.EffectiveDurationSeconds.Value,
            "study-session",
            session.Id.ToString("D"),
            session.CompletionNote,
            occurredAtUtc);
    }

    public static LedgerEntry CreateAdjustment(
        long amountSeconds,
        string note,
        DateTimeOffset occurredAtUtc,
        Guid? id = null)
    {
        return Create(
            id ?? Guid.NewGuid(),
            LedgerEntryType.Adjustment,
            amountSeconds,
            "manual",
            Guid.NewGuid().ToString("D"),
            DomainGuards.NonEmpty(note, nameof(note)),
            occurredAtUtc);
    }

    public static LedgerEntry CreateSpend(
        long amountSeconds,
        string sourceId,
        string note,
        DateTimeOffset occurredAtUtc,
        Guid? id = null)
    {
        return Create(
            id ?? Guid.NewGuid(),
            LedgerEntryType.Spend,
            -DomainGuards.Positive(amountSeconds, nameof(amountSeconds)),
            "reward-redemption",
            DomainGuards.NonEmpty(sourceId, nameof(sourceId)),
            DomainGuards.NonEmpty(note, nameof(note)),
            occurredAtUtc);
    }

    public static LedgerEntry ForGameRound(
        long netAmountSeconds,
        Guid roundId,
        string note,
        DateTimeOffset occurredAtUtc,
        Guid? id = null)
    {
        if (roundId == Guid.Empty)
        {
            throw new ArgumentException("Game round ID cannot be empty.", nameof(roundId));
        }

        if (netAmountSeconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(netAmountSeconds), "Game result cannot be neutral.");
        }

        return Create(
            id ?? Guid.NewGuid(),
            netAmountSeconds > 0 ? LedgerEntryType.GameWin : LedgerEntryType.GameLoss,
            netAmountSeconds,
            "game-round",
            roundId.ToString("D"),
            DomainGuards.NonEmpty(note, nameof(note)),
            occurredAtUtc);
    }

    private static LedgerEntry Create(
        Guid id,
        LedgerEntryType type,
        long amountSeconds,
        string sourceType,
        string sourceId,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ledger entry ID cannot be empty.", nameof(id));
        }

        if (amountSeconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountSeconds), "Ledger amount cannot be zero.");
        }

        if (type is (LedgerEntryType.Earn or LedgerEntryType.GameWin) && amountSeconds < 0)
        {
            throw new DomainRuleException("Earning entries must be positive.");
        }

        if (type is (LedgerEntryType.Spend or LedgerEntryType.GameLoss) && amountSeconds > 0)
        {
            throw new DomainRuleException("Spending entries must be negative.");
        }

        return new LedgerEntry(
            id,
            type,
            amountSeconds,
            DomainGuards.NonEmpty(sourceType, nameof(sourceType)),
            DomainGuards.NonEmpty(sourceId, nameof(sourceId)),
            note,
            DomainGuards.Utc(occurredAtUtc, nameof(occurredAtUtc)));
    }
}
