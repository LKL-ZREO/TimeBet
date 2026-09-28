using System.Text.RegularExpressions;

namespace TimeBet.Application.Relevance;

public static class TaskRelevanceService
{
    private static readonly HashSet<string> DistractingProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "weixin", "wechat", "qq", "discord", "telegram", "steam",
        "epicgameslauncher", "tiktok", "douyin", "netflix", "spotify",
    };

    private static readonly HashSet<string> StudyProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "winword", "onenote", "notion", "obsidian",
        "acrobat", "acrord32", "excel", "powerpnt", "idea64", "pycharm64",
        "terminal", "powershell", "windowsterminal", "sumatrapdf",
    };

    private static readonly HashSet<string> GenericGoalWords = new(StringComparer.Ordinal)
    {
        "完成", "今天", "任务", "学习", "复习", "整理", "推进", "一下", "章节", "内容", "作业",
    };

    public static TaskRelevanceSnapshot Evaluate(
        string goal,
        string processName,
        string windowTitle,
        bool isIdle,
        DateTimeOffset observedAtUtc)
    {
        var normalizedGoal = Normalize(goal);
        var normalizedTitle = Normalize(windowTitle);
        var normalizedProcess = NormalizeProcess(processName);
        var reasons = new List<string>();

        if (isIdle)
        {
            reasons.Add("超过空闲阈值，暂时没有检测到输入。时间仍在计时，但不能视为有效学习证据。");
            return Build(goal, processName, windowTitle, "idle", 0, true, observedAtUtc, reasons);
        }

        var matchedKeywords = ExtractKeywords(goal)
            .Where(keyword => normalizedTitle.Contains(keyword, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(normalizedGoal) && normalizedTitle.Contains(normalizedGoal, StringComparison.Ordinal))
        {
            if (DistractingProcesses.Contains(normalizedProcess))
            {
                reasons.Add("窗口标题包含当前任务描述，但所在应用也可能承载无关内容，需要结合实际用途确认。");
                return Build(goal, processName, windowTitle, "possible", 62, false, observedAtUtc, reasons);
            }

            reasons.Add("窗口标题包含完整的当前任务描述。");
            return Build(goal, processName, windowTitle, "high", 95, false, observedAtUtc, reasons);
        }

        if (matchedKeywords.Length > 0)
        {
            if (DistractingProcesses.Contains(normalizedProcess))
            {
                reasons.Add($"窗口标题包含任务关键词（{string.Join("、", matchedKeywords.Take(3))}），但当前应用的用途仍需确认。");
                return Build(goal, processName, windowTitle, "possible", 60, false, observedAtUtc, reasons);
            }

            reasons.Add($"窗口标题包含任务关键词：{string.Join("、", matchedKeywords.Take(3))}。");
            return Build(goal, processName, windowTitle, "high", 82, false, observedAtUtc, reasons);
        }

        if (DistractingProcesses.Contains(normalizedProcess))
        {
            reasons.Add("当前应用通常不是学习编辑或阅读工具，可能需要确认是否仍在推进当前任务。");
            return Build(goal, processName, windowTitle, "low", 18, false, observedAtUtc, reasons);
        }

        if (StudyProcesses.Contains(normalizedProcess))
        {
            reasons.Add("当前应用常用于阅读、编辑或编程，但窗口标题没有提供足够的任务内容证据。");
            return Build(goal, processName, windowTitle, "possible", 58, false, observedAtUtc, reasons);
        }

        reasons.Add("已记录当前窗口，但仅凭进程名和标题无法判断它是否与任务相关。");
        return Build(goal, processName, windowTitle, "unknown", 50, false, observedAtUtc, reasons);
    }

    public static TaskRelevanceSnapshot Disabled(string goal, DateTimeOffset observedAtUtc)
    {
        return new TaskRelevanceSnapshot(
            false,
            goal,
            string.Empty,
            string.Empty,
            "disabled",
            0,
            false,
            observedAtUtc,
            ["活动记录已关闭，TimeBet 不会读取当前前台窗口。"]);
    }

    private static TaskRelevanceSnapshot Build(
        string goal,
        string processName,
        string windowTitle,
        string level,
        int score,
        bool isIdle,
        DateTimeOffset observedAtUtc,
        IReadOnlyList<string> reasons)
    {
        return new TaskRelevanceSnapshot(
            true,
            goal,
            processName,
            windowTitle,
            level,
            score,
            isIdle,
            observedAtUtc,
            reasons);
    }

    private static string[] ExtractKeywords(string goal)
    {
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(goal, "[\\p{IsCJKUnifiedIdeographs}]{2,}|[A-Za-z0-9]{2,}"))
        {
            var value = match.Value.Trim();
            if (value.Length >= 2 && !GenericGoalWords.Contains(value))
            {
                keywords.Add(value);
            }

            if (value.Any(char.IsLetterOrDigit) && value.Length >= 3)
            {
                for (var index = 0; index < value.Length - 1; index++)
                {
                    var token = value.Substring(index, 2);
                    if (!GenericGoalWords.Contains(token))
                    {
                        keywords.Add(token);
                    }
                }
            }
        }

        return keywords.OrderByDescending(value => value.Length).ToArray();
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray());
    }

    private static string NormalizeProcess(string value)
    {
        return Normalize(Path.GetFileNameWithoutExtension(value.Trim()));
    }
}
