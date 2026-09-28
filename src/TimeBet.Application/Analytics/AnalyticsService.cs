using System.Globalization;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Sessions;

namespace TimeBet.Application.Analytics;

public sealed class AnalyticsService
{
    private const int MaximumLedgerPageSize = 100;
    private readonly IAnalyticsRepository analytics;
    private readonly IClock clock;
    private readonly TimeZoneInfo timeZone;

    public AnalyticsService(
        IAnalyticsRepository analytics,
        IClock clock,
        TimeZoneInfo? timeZone = null)
    {
        this.analytics = analytics;
        this.clock = clock;
        this.timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public async Task<AnalyticsOverviewSnapshot> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow.ToUniversalTime();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).Date);
        var trendStartDate = today.AddDays(-6);
        var weekStartDate = today.AddDays(-DaysSinceMonday(today.DayOfWeek));
        var queryStartDate = trendStartDate < weekStartDate ? trendStartDate : weekStartDate;
        var queryStartUtc = StartOfDayUtc(queryStartDate);
        var todayStartUtc = StartOfDayUtc(today);
        var todayEndUtc = StartOfDayUtc(today.AddDays(1));
        var weekStartUtc = StartOfDayUtc(weekStartDate);

        var sessionsTask = analytics.GetStudySessionsAsync(nowUtc, queryStartUtc, cancellationToken);
        var activitiesTask = analytics.GetActivitiesAsync(todayEndUtc, todayStartUtc, cancellationToken);
        var ledgerTask = analytics.GetLedgerTotalsAsync(cancellationToken);
        await Task.WhenAll(sessionsTask, activitiesTask, ledgerTask);

        var sessions = await sessionsTask;
        var studyIntervals = sessions.SelectMany(session => GetStudyIntervals(session, nowUtc)).ToArray();
        var dailyTrend = Enumerable.Range(0, 7)
            .Select(index => BuildDailySnapshot(trendStartDate.AddDays(index), sessions, studyIntervals, nowUtc))
            .ToArray();

        return new AnalyticsOverviewSnapshot(
            nowUtc,
            todayStartUtc,
            weekStartUtc,
            SumOverlapSeconds(studyIntervals, todayStartUtc, Min(todayEndUtc, nowUtc)),
            SumOverlapSeconds(studyIntervals, weekStartUtc, nowUtc),
            CountCompletedSessions(sessions, todayStartUtc, Min(todayEndUtc, nowUtc)),
            CountCompletedSessions(sessions, weekStartUtc, nowUtc),
            await ledgerTask,
            dailyTrend,
            BuildApplicationUsage(await activitiesTask, todayStartUtc, Min(todayEndUtc, nowUtc)));
    }

    public Task<IReadOnlyList<LedgerEntrySnapshot>> GetLedgerEntriesAsync(
        int count = 40,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > MaximumLedgerPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                $"账本明细数量必须在 1 到 {MaximumLedgerPageSize} 之间。");
        }

        return analytics.GetLedgerEntriesAsync(count, cancellationToken);
    }

    public async Task<string> CreateSummaryCsvAsync(CancellationToken cancellationToken = default)
    {
        var overviewTask = GetOverviewAsync(cancellationToken);
        var entriesTask = analytics.GetLedgerEntriesAsync(null, cancellationToken);
        await Task.WhenAll(overviewTask, entriesTask);
        return SummaryCsvWriter.Write(await overviewTask, await entriesTask, timeZone);
    }

    private DailyStudySnapshot BuildDailySnapshot(
        DateOnly date,
        IReadOnlyList<AnalyticsStudySessionRecord> sessions,
        IReadOnlyList<StudyInterval> intervals,
        DateTimeOffset nowUtc)
    {
        var startUtc = StartOfDayUtc(date);
        var endUtc = Min(StartOfDayUtc(date.AddDays(1)), nowUtc);
        return new DailyStudySnapshot(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            startUtc,
            SumOverlapSeconds(intervals, startUtc, endUtc),
            CountCompletedSessions(sessions, startUtc, endUtc));
    }

    private DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static int DaysSinceMonday(DayOfWeek dayOfWeek)
    {
        return ((int)dayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
    }

    private static List<StudyInterval> GetStudyIntervals(
        AnalyticsStudySessionRecord session,
        DateTimeOffset nowUtc)
    {
        var intervals = new List<StudyInterval>();
        DateTimeOffset? runningSince = null;
        foreach (var sessionEvent in session.Events.OrderBy(item => item.OccurredAtUtc))
        {
            switch (sessionEvent.Type)
            {
                case SessionEventType.Started:
                case SessionEventType.Resumed:
                    runningSince = sessionEvent.OccurredAtUtc;
                    break;
                case SessionEventType.Paused:
                case SessionEventType.Completed:
                    if (runningSince is not null && sessionEvent.OccurredAtUtc > runningSince)
                    {
                        intervals.Add(new StudyInterval(runningSince.Value, sessionEvent.OccurredAtUtc));
                    }

                    runningSince = null;
                    break;
                default:
                    throw new InvalidOperationException($"不支持的会话事件：{sessionEvent.Type}");
            }
        }

        if (runningSince is not null && nowUtc > runningSince)
        {
            intervals.Add(new StudyInterval(runningSince.Value, nowUtc));
        }

        return intervals;
    }

    private static long SumOverlapSeconds(
        IEnumerable<StudyInterval> intervals,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        if (rangeEndUtc <= rangeStartUtc)
        {
            return 0;
        }

        return intervals.Sum(interval => OverlapSeconds(
            interval.StartedAtUtc,
            interval.EndedAtUtc,
            rangeStartUtc,
            rangeEndUtc));
    }

    private static int CountCompletedSessions(
        IEnumerable<AnalyticsStudySessionRecord> sessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        if (rangeEndUtc <= rangeStartUtc)
        {
            return 0;
        }

        return sessions.Count(session => session.Events.Any(sessionEvent =>
            sessionEvent.Type == SessionEventType.Completed &&
            sessionEvent.OccurredAtUtc >= rangeStartUtc &&
            sessionEvent.OccurredAtUtc < rangeEndUtc));
    }

    private static ApplicationUsageSnapshot[] BuildApplicationUsage(
        IEnumerable<AnalyticsActivityRecord> activities,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var grouped = activities
            .Where(activity => !activity.IsIdle)
            .GroupBy(activity => activity.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ProcessName = group.First().ProcessName,
                DurationSeconds = group.Sum(activity => OverlapSeconds(
                    activity.StartedAtUtc,
                    activity.EndedAtUtc,
                    rangeStartUtc,
                    rangeEndUtc)),
            })
            .Where(item => item.DurationSeconds > 0)
            .OrderByDescending(item => item.DurationSeconds)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var total = grouped.Sum(item => item.DurationSeconds);

        return grouped
            .Select(item => new ApplicationUsageSnapshot(
                item.ProcessName,
                item.DurationSeconds,
                total == 0 ? 0 : (double)item.DurationSeconds / total))
            .ToArray();
    }

    private static long OverlapSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var start = startedAtUtc > rangeStartUtc ? startedAtUtc : rangeStartUtc;
        var end = endedAtUtc < rangeEndUtc ? endedAtUtc : rangeEndUtc;
        return end <= start ? 0 : (long)Math.Floor((end - start).TotalSeconds);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }

    private sealed record StudyInterval(DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc);
}
