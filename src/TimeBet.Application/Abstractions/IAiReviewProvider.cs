using TimeBet.Application.AI;
using TimeBet.Application.Reviews;

namespace TimeBet.Application.Abstractions;

public interface IAiReviewProvider
{
    Task<AiReviewText> GenerateAsync(
        DailyReviewSnapshot review,
        StoredAiSettings settings,
        CancellationToken cancellationToken = default);
}
