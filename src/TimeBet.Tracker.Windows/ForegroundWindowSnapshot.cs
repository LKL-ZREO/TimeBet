namespace TimeBet.Tracker.Windows;

public sealed record ForegroundWindowSnapshot(
    nint Handle,
    string ProcessName,
    string WindowTitle,
    TimeSpan IdleDuration,
    int Left,
    int Top,
    int Width,
    int Height);
