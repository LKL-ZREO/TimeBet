using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using TimeBet.Application.Analytics;

namespace TimeBet.Desktop.Export;

public sealed class SummaryExportService
{
    private readonly AnalyticsService analytics;

    public SummaryExportService(AnalyticsService analytics)
    {
        this.analytics = analytics;
    }

    public async Task<SummaryExportResult> ExportAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var dialog = new SaveFileDialog
        {
            Title = "导出 TimeBet 学习摘要",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"TimeBet-学习摘要-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return new SummaryExportResult(false, null);
        }

        var csv = await analytics.CreateSummaryCsvAsync(cancellationToken);
        await File.WriteAllTextAsync(
            dialog.FileName,
            csv,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        return new SummaryExportResult(true, Path.GetFileName(dialog.FileName));
    }
}

public sealed record SummaryExportResult(bool Saved, string? FileName);
