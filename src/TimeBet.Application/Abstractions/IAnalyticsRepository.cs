using TimeBet.Application.Analytics;
using TimeBet.Domain.Sessions;

namespace TimeBet.Application.Abstractions;

public interface IAnalyticsRepository
{
    Task<IReadOnlyList<AnalyticsStudySessionRecord>> GetStudySessionsAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnalyticsActivityRecord>> GetActivitiesAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default);

    Task<LedgerTotalsRecord> GetLedgerTotalsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerEntrySnapshot>> GetLedgerEntriesAsync(
        int? count,
        CancellationToken cancellationToken = default);
}

public sealed record AnalyticsSessionEventRecord(
    SessionEventType Type,
    DateTimeOffset OccurredAtUtc);

public sealed record AnalyticsStudySessionRecord(
    Guid Id,
    string Goal,
    IReadOnlyList<AnalyticsSessionEventRecord> Events);

public sealed record AnalyticsActivityRecord(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string ProcessName,
    bool IsIdle);
