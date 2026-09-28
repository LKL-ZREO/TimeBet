namespace TimeBet.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
