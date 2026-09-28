using TimeBet.Application.Analytics;
using TimeBet.Application.Abstractions;
using TimeBet.Application.Relevance;
using TimeBet.Application.Tasks;
using TimeBet.Application.Timeline;

namespace TimeBet.Application.Reviews;

public sealed class DailyReviewService
{
    private readonly AnalyticsService analytics;
    private readonly PlannerService planner;
    private readonly TimelineService timeline;
    private readonly IClock clock;

    public DailyReviewService(
        AnalyticsService analytics,
        PlannerService planner,
        TimelineService timeline,
        IClock clock)
    {
        this.analytics = analytics;
        this.planner = planner;
        this.timeline = timeline;
        this.clock = clock;
    }

    public async Task<DailyReviewSnapshot> GetTodayAsync(
        CancellationToken cancellationToken = default)
    {
        var analyticsTask = analytics.GetOverviewAsync(cancellationToken);
        var planTask = planner.GetTodayAsync(cancellationToken);
        var timelineTask = timeline.GetOverviewAsync(cancellationToken);
        await Task.WhenAll(analyticsTask, planTask, timelineTask);

        var overview = await analyticsTask;
        var plan = await planTask;
        var tracking = await timelineTask;
        var sessions = tracking.Timeline.Sessions
            .Select(BuildSession)
            .ToArray();
        var applications = BuildApplicationUsage(tracking.Timeline.Sessions);
        var relevance = BuildRelevance(tracking.Settings, sessions);
        var metrics = new DailyReviewMetrics(
            plan.PlannedSeconds,
            overview.TodayStudySeconds,
            tracking.Timeline.TrackedSeconds,
            tracking.Timeline.IdleSeconds,
            plan.Blocks.Count,
            plan.CompletedCount,
            plan.SkippedCount,
            overview.TodayCompletedSessions);
        var hasData = plan.Blocks.Count > 0 ||
                      overview.TodayStudySeconds > 0 ||
                      tracking.Timeline.Sessions.Count > 0;

        return new DailyReviewSnapshot(
            plan.Date,
            clock.UtcNow.ToUniversalTime(),
            hasData,
            "local",
            null,
            BuildHeadline(hasData, metrics, relevance),
            BuildNarrative(hasData, metrics, relevance, applications),
            metrics,
            relevance,
            applications,
            sessions,
            BuildHighlights(hasData, plan, metrics, relevance, applications),
            BuildSuggestions(hasData, plan, metrics, relevance));
    }

    private static DailyReviewSession BuildSession(TimelineSessionSnapshot session)
    {
        var levels = session.Activities
            .Select(activity => new
            {
                DurationSeconds = DurationSeconds(activity.StartedAtUtc, activity.EndedAtUtc),
                Relevance = TaskRelevanceService.Evaluate(
                    session.Goal,
                    activity.ProcessName,
                    activity.WindowTitle,
                    activity.IsIdle,
                    activity.EndedAtUtc),
            })
            .Where(item => item.DurationSeconds > 0)
            .ToArray();

        return new DailyReviewSession(
            session.Id,
            session.Goal,
            session.Status,
            session.EffectiveDurationSeconds,
            levels.Where(item => item.Relevance.Level == "idle").Sum(item => item.DurationSeconds),
            levels.Where(item => item.Relevance.Level == "high").Sum(item => item.DurationSeconds),
            levels.Where(item => item.Relevance.Level == "possible").Sum(item => item.DurationSeconds),
            levels.Where(item => item.Relevance.Level == "low").Sum(item => item.DurationSeconds),
            levels.Where(item => item.Relevance.Level == "unknown").Sum(item => item.DurationSeconds),
            BuildApplicationUsage([session]));
    }

    private static DailyReviewRelevance BuildRelevance(
        TrackingSettings settings,
        IReadOnlyList<DailyReviewSession> sessions)
    {
        var high = sessions.Sum(session => session.HighSeconds);
        var possible = sessions.Sum(session => session.PossibleSeconds);
        var low = sessions.Sum(session => session.LowSeconds);
        var unknown = sessions.Sum(session => session.UnknownSeconds);
        return new DailyReviewRelevance(
            settings.CaptureActivity,
            high + possible + low + unknown,
            high,
            possible,
            low,
            unknown);
    }

    private static DailyReviewApplication[] BuildApplicationUsage(
        IEnumerable<TimelineSessionSnapshot> sessions)
    {
        var grouped = sessions
            .SelectMany(session => session.Activities)
            .Where(activity => !activity.IsIdle)
            .GroupBy(activity => activity.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ProcessName = group.First().ProcessName,
                DurationSeconds = group.Sum(activity =>
                    DurationSeconds(activity.StartedAtUtc, activity.EndedAtUtc)),
            })
            .Where(item => item.DurationSeconds > 0)
            .OrderByDescending(item => item.DurationSeconds)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var total = grouped.Sum(item => item.DurationSeconds);

        return grouped
            .Select(item => new DailyReviewApplication(
                item.ProcessName,
                item.DurationSeconds,
                total == 0 ? 0 : (double)item.DurationSeconds / total))
            .ToArray();
    }

    private static string BuildHeadline(
        bool hasData,
        DailyReviewMetrics metrics,
        DailyReviewRelevance relevance)
    {
        if (!hasData)
        {
            return "今天还没有形成可回顾的记录";
        }

        if (!relevance.ActivityTrackingEnabled)
        {
            return metrics.StudySeconds > 0
                ? "今天已经完成学习记录，活动证据尚未开启"
                : "今天已有计划，等待开始第一次专注";
        }

        if (relevance.EvaluatedSeconds == 0)
        {
            return metrics.StudySeconds > 0
                ? "今天已经投入学习，暂时缺少足够的窗口证据"
                : "今天已有计划，等待开始第一次专注";
        }

        var lowShare = Share(relevance.LowSeconds, relevance.EvaluatedSeconds);
        var supportedShare = Share(
            relevance.HighSeconds + relevance.PossibleSeconds,
            relevance.EvaluatedSeconds);
        var idleShare = Share(metrics.IdleSeconds, metrics.TrackedSeconds);
        if (lowShare >= 0.2)
        {
            return "今天有实际推进，也有部分时段值得回看";
        }

        if (idleShare >= 0.25)
        {
            return "今天完成了学习投入，连续性还可以再加强";
        }

        return supportedShare >= 0.7
            ? "今天的前台活动与设定任务总体一致"
            : "今天已经留下了一份可复盘的学习记录";
    }

    private static string BuildNarrative(
        bool hasData,
        DailyReviewMetrics metrics,
        DailyReviewRelevance relevance,
        DailyReviewApplication[] applications)
    {
        if (!hasData)
        {
            return "创建今天的计划并开始计时后，这里会自动结合任务、时长和前台软件生成回顾。";
        }

        var parts = new List<string>();
        if (metrics.StudySeconds > 0)
        {
            parts.Add($"今天累计学习 {FormatDuration(metrics.StudySeconds)}，完成 {metrics.CompletedSessionCount} 个学习时间块。");
        }
        else
        {
            parts.Add("今天已经安排了任务，但还没有产生有效学习时长。");
        }

        if (metrics.PlannedBlockCount > 0)
        {
            parts.Add($"计划 {FormatDuration(metrics.PlannedSeconds)}，已完成 {metrics.CompletedBlockCount}/{metrics.PlannedBlockCount} 个计划块。");
        }

        if (!relevance.ActivityTrackingEnabled)
        {
            parts.Add("活动记录处于关闭状态，因此本次回顾不判断实际使用的软件是否与任务一致。");
        }
        else if (relevance.EvaluatedSeconds > 0)
        {
            parts.Add(
                $"在 {FormatDuration(relevance.EvaluatedSeconds)} 的非空闲前台记录中，" +
                $"较强相关证据 {FormatDuration(relevance.HighSeconds)}、" +
                $"可能相关 {FormatDuration(relevance.PossibleSeconds)}、" +
                $"可能偏离 {FormatDuration(relevance.LowSeconds)}、" +
                $"暂无法判断 {FormatDuration(relevance.UnknownSeconds)}。");
        }

        if (applications.Length > 0)
        {
            var main = applications[0];
            parts.Add($"使用最多的前台软件是 {main.ProcessName}，累计 {FormatDuration(main.DurationSeconds)}。");
        }

        return string.Join("", parts);
    }

    private static string[] BuildHighlights(
        bool hasData,
        TodayPlanSnapshot plan,
        DailyReviewMetrics metrics,
        DailyReviewRelevance relevance,
        DailyReviewApplication[] applications)
    {
        if (!hasData)
        {
            return [];
        }

        var highlights = new List<string>();
        var completedTasks = plan.Blocks
            .Where(block => block.Status == "completed")
            .Select(block => block.TaskTitle)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        if (completedTasks.Length > 0)
        {
            highlights.Add($"完成了：{string.Join("、", completedTasks.Select(title => $"「{title}」"))}");
        }

        if (metrics.StudySeconds > 0)
        {
            highlights.Add($"留下 {FormatDuration(metrics.StudySeconds)} 的有效学习记录");
        }

        if (applications.Length > 0)
        {
            highlights.Add($"主要软件：{string.Join("、", applications.Take(3).Select(item => $"{item.ProcessName} {FormatDuration(item.DurationSeconds)}"))}");
        }

        if (relevance.EvaluatedSeconds > 0 && relevance.HighSeconds > 0)
        {
            highlights.Add($"较强任务相关证据覆盖 {Percent(relevance.HighSeconds, relevance.EvaluatedSeconds)} 的可判断前台时长");
        }

        return highlights.Take(4).ToArray();
    }

    private static string[] BuildSuggestions(
        bool hasData,
        TodayPlanSnapshot plan,
        DailyReviewMetrics metrics,
        DailyReviewRelevance relevance)
    {
        if (!hasData)
        {
            return ["先安排一个明确任务，再开始计时；目标越具体，后续的相关性判断越可靠。"];
        }

        var suggestions = new List<string>();
        if (plan.PlannedCount > 0)
        {
            suggestions.Add($"今天还有 {plan.PlannedCount} 个计划块未完成，可决定继续、顺延或跳过，避免计划一直悬空。");
        }

        if (relevance.ActivityTrackingEnabled && relevance.EvaluatedSeconds > 0)
        {
            if (Share(relevance.LowSeconds, relevance.EvaluatedSeconds) >= 0.1)
            {
                suggestions.Add("有一部分前台时长被标为“可能偏离”，建议结合当时用途确认是否确实在推进任务。");
            }

            if (Share(relevance.UnknownSeconds, relevance.EvaluatedSeconds) >= 0.3)
            {
                suggestions.Add("无法判断的时长较多，下次可把任务目标写得更具体，例如加入章节、文件名或题目关键词。");
            }
        }

        if (Share(metrics.IdleSeconds, metrics.TrackedSeconds) >= 0.2)
        {
            suggestions.Add("空闲记录占比较高；离开电脑时可以暂停计时，让有效时长更接近真实投入。");
        }

        if (metrics.PlannedSeconds > 0 && metrics.StudySeconds > metrics.PlannedSeconds * 1.25)
        {
            suggestions.Add("实际投入明显高于计划，可以适当上调同类任务的预计时长，减少以后排程偏差。");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("今天的记录暂未显示明显问题，下一次继续保持明确目标和及时暂停即可。");
        }

        return suggestions.Take(3).ToArray();
    }

    private static long DurationSeconds(DateTimeOffset start, DateTimeOffset end)
    {
        return end <= start ? 0 : (long)Math.Floor((end - start).TotalSeconds);
    }

    private static double Share(long part, long total)
    {
        return total <= 0 ? 0 : (double)part / total;
    }

    private static string Percent(long part, long total)
    {
        return $"{Math.Round(Share(part, total) * 100):0}%";
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return "0 分钟";
        }

        var minutes = Math.Max(1, (long)Math.Round(seconds / 60d));
        var hours = minutes / 60;
        var remainder = minutes % 60;
        if (hours == 0)
        {
            return $"{minutes} 分钟";
        }

        return remainder == 0 ? $"{hours} 小时" : $"{hours} 小时 {remainder} 分钟";
    }
}
