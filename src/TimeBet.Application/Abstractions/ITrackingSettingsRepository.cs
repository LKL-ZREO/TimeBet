using TimeBet.Application.Timeline;

namespace TimeBet.Application.Abstractions;

public interface ITrackingSettingsRepository
{
    Task<TrackingSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TrackingSettings settings, CancellationToken cancellationToken = default);
}
