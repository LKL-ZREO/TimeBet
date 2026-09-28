namespace TimeBet.Application.Relevance;

public sealed record TaskRelevanceSnapshot(
    bool Available,
    string Goal,
    string ProcessName,
    string WindowTitle,
    string Level,
    int Score,
    bool IsIdle,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<string> Reasons);
