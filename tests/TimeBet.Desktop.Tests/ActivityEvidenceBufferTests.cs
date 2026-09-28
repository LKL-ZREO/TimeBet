using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop.Tests;

public sealed class ActivityEvidenceBufferTests
{
    [Fact]
    public void TimeBetWindowDoesNotReplaceLastWorkEvidence()
    {
        var buffer = new ActivityEvidenceBuffer(["TimeBet.Desktop"]);
        var workTime = DateTimeOffset.UtcNow.AddSeconds(-5);
        buffer.Observe(Window("WINWORD", "高数错题.docx"), workTime);
        buffer.Observe(Window("TimeBet.Desktop", "TimeBet"), DateTimeOffset.UtcNow);

        var latest = Assert.IsType<ForegroundWindowEvidence>(buffer.GetLatest());
        Assert.Equal("WINWORD", latest.ProcessName);
        Assert.Equal("高数错题.docx", latest.WindowTitle);
        Assert.Equal(workTime, latest.ObservedAtUtc);
    }

    private static ForegroundWindowSnapshot Window(string processName, string title)
    {
        return new ForegroundWindowSnapshot(
            nint.Zero,
            processName,
            title,
            TimeSpan.Zero,
            0,
            0,
            1200,
            800);
    }
}
