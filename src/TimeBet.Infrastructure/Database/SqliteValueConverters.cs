using TimeBet.Domain.Ledger;
using TimeBet.Domain.Sessions;

namespace TimeBet.Infrastructure.Database;

internal static class SqliteValueConverters
{
    public static long ToUnixMilliseconds(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToUnixTimeMilliseconds();
    }

    public static DateTimeOffset FromUnixMilliseconds(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    public static string ToDatabaseValue(this StudySessionStatus status)
    {
        return status switch
        {
            StudySessionStatus.Running => "running",
            StudySessionStatus.Paused => "paused",
            StudySessionStatus.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    public static StudySessionStatus ToStudySessionStatus(string value)
    {
        return value switch
        {
            "running" => StudySessionStatus.Running,
            "paused" => StudySessionStatus.Paused,
            "completed" => StudySessionStatus.Completed,
            _ => throw new InvalidOperationException($"Unknown session status '{value}'."),
        };
    }

    public static SessionEventType ToSessionEventType(string value)
    {
        return value switch
        {
            "started" => SessionEventType.Started,
            "paused" => SessionEventType.Paused,
            "resumed" => SessionEventType.Resumed,
            "completed" => SessionEventType.Completed,
            _ => throw new InvalidOperationException($"Unknown session event type '{value}'."),
        };
    }

    public static string ToDatabaseValue(this SessionEventType type)
    {
        return type switch
        {
            SessionEventType.Started => "started",
            SessionEventType.Paused => "paused",
            SessionEventType.Resumed => "resumed",
            SessionEventType.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static string ToDatabaseValue(this LedgerEntryType type)
    {
        return type switch
        {
            LedgerEntryType.Earn => "earn",
            LedgerEntryType.Spend => "spend",
            LedgerEntryType.GameWin => "game-win",
            LedgerEntryType.GameLoss => "game-loss",
            LedgerEntryType.Adjustment => "adjustment",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static LedgerEntryType ToLedgerEntryType(string value)
    {
        return value switch
        {
            "earn" => LedgerEntryType.Earn,
            "spend" => LedgerEntryType.Spend,
            "game-win" => LedgerEntryType.GameWin,
            "game-loss" => LedgerEntryType.GameLoss,
            "adjustment" => LedgerEntryType.Adjustment,
            _ => throw new InvalidOperationException($"Unknown ledger entry type '{value}'."),
        };
    }
}
