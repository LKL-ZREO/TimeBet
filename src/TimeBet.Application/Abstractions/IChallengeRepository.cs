using TimeBet.Application.Challenges;
using TimeBet.Domain.Challenges;

namespace TimeBet.Application.Abstractions;

public interface IChallengeRepository
{
    Task<ChallengeStateRecord> GetStateAsync(
        DateTimeOffset dayStartedAtUtc,
        DateTimeOffset dayEndedAtUtc,
        int historyCount,
        CancellationToken cancellationToken = default);

    Task SetEnabledAsync(
        bool enabled,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ChallengePlayCommit> PlayAsync(
        ChallengePlayCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record ChallengeStateRecord(
    bool Enabled,
    long BalanceSeconds,
    bool HasActiveStudySession,
    int RoundsPlayedToday,
    IReadOnlyList<ChallengeRoundRecord> RecentRounds);

public sealed record ChallengeRoundRecord(
    GameRound Round,
    long NetSeconds);

public sealed record ChallengePlayCommand(
    Guid RoundId,
    long WagerSeconds,
    decimal Outcome,
    DateTimeOffset PlayedAtUtc,
    DateTimeOffset DayStartedAtUtc,
    DateTimeOffset DayEndedAtUtc);

public sealed record ChallengePlayCommit(
    ChallengeRoundRecord Round,
    long BalanceSeconds,
    bool WasAlreadyCommitted);
