using TimeBet.Application.Abstractions;

namespace TimeBet.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
