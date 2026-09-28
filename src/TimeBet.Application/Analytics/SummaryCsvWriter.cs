using System.Globalization;
using System.Text;

namespace TimeBet.Application.Analytics;

internal static class SummaryCsvWriter
{
    public static string Write(
        AnalyticsOverviewSnapshot overview,
        IReadOnlyList<LedgerEntrySnapshot> ledgerEntries,
        TimeZoneInfo timeZone)
    {
        var csv = new StringBuilder();
        AppendRow(csv, "TimeBet 学习摘要");
        AppendRow(csv, "生成时间", FormatLocal(overview.GeneratedAtUtc, timeZone));
        csv.AppendLine();

        AppendRow(csv, "概览", "秒数");
        AppendRow(csv, "今日学习", overview.TodayStudySeconds);
        AppendRow(csv, "本周学习", overview.WeekStudySeconds);
        AppendRow(csv, "可用余额", overview.Ledger.BalanceSeconds);
        AppendRow(csv, "累计收入", overview.Ledger.EarnedSeconds);
        AppendRow(csv, "累计消费", overview.Ledger.SpentSeconds);
        csv.AppendLine();

        AppendRow(csv, "近 7 天", "学习秒数", "完成时间块");
        foreach (var day in overview.DailyTrend)
        {
            AppendRow(csv, day.Date, day.StudySeconds, day.CompletedSessions);
        }

        csv.AppendLine();
        AppendRow(csv, "今日应用", "使用秒数", "占比");
        foreach (var application in overview.ApplicationUsage)
        {
            AppendRow(
                csv,
                application.ProcessName,
                application.DurationSeconds,
                application.Share.ToString("P1", CultureInfo.GetCultureInfo("zh-CN")));
        }

        csv.AppendLine();
        AppendRow(csv, "账本类型", "变动秒数", "来源", "说明", "发生时间");
        foreach (var entry in ledgerEntries)
        {
            AppendRow(
                csv,
                entry.Type,
                entry.AmountSeconds,
                entry.SourceType,
                entry.Note ?? string.Empty,
                FormatLocal(entry.OccurredAtUtc, timeZone));
        }

        return csv.ToString();
    }

    private static string FormatLocal(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        return TimeZoneInfo.ConvertTime(value, timeZone)
            .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private static void AppendRow(StringBuilder csv, params object[] values)
    {
        csv.AppendLine(string.Join(',', values.Select(value => Escape(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))));
    }

    private static string Escape(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
