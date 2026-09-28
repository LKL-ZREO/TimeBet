namespace TimeBet.Tracker.Windows;

public sealed record ForegroundWindowEvidence(
    string ProcessName,
    string WindowTitle,
    TimeSpan IdleDuration,
    DateTimeOffset ObservedAtUtc);

public interface IActivityEvidenceSource
{
    ForegroundWindowEvidence? GetLatest();
}

public sealed class ActivityEvidenceBuffer : IActivityEvidenceSource
{
    private readonly HashSet<string> ignoredProcesses;
    private readonly object gate = new();
    private ForegroundWindowEvidence? latest;

    public ActivityEvidenceBuffer(IEnumerable<string>? ignoredProcesses = null)
    {
        this.ignoredProcesses = new HashSet<string>(
            ignoredProcesses ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public void Observe(ForegroundWindowSnapshot window, DateTimeOffset observedAtUtc)
    {
        if (ignoredProcesses.Contains(window.ProcessName))
        {
            return;
        }

        lock (gate)
        {
            latest = new ForegroundWindowEvidence(
                window.ProcessName,
                window.WindowTitle,
                window.IdleDuration,
                observedAtUtc);
        }
    }

    public ForegroundWindowEvidence? GetLatest()
    {
        lock (gate)
        {
            return latest;
        }
    }
}
