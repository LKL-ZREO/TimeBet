using System.Security.Cryptography;
using TimeBet.Application.Abstractions;

namespace TimeBet.Infrastructure;

public sealed class CryptographicRandomSource : IRandomSource
{
    public double NextUnitInterval()
    {
        return RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue;
    }
}
