using TimeBet.Application.Timeline;
using TimeBet.Domain.Timeline;

namespace TimeBet.Application.Abstractions;

public interface IActivityTimelineRepository
{
    Task AppendOrMergeActivityAsync(
        ActivitySegment segment,
        CancellationToken cancellationToken = default);

    Task AddScreenshotAsync(
        ScreenshotRecord screenshot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineSessionRecord>> GetSessionsAsync(
        DateTimeOffset startedBeforeUtc,
        DateTimeOffset endedAfterUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DeleteScreenshotsBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default);
}
