using TimeBet.Application.AI;

namespace TimeBet.Application.Abstractions;

public interface IAiSettingsRepository
{
    Task<StoredAiSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StoredAiSettings settings, CancellationToken cancellationToken = default);
}
