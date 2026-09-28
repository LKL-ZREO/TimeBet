namespace TimeBet.Application.AI;

public sealed record AiSettingsSnapshot(
    bool Enabled,
    string BaseUrl,
    string Model,
    bool ApiKeyConfigured,
    string? ApiKeyHint);

public sealed record UpdateAiSettingsRequest(
    bool Enabled,
    string? BaseUrl,
    string? Model,
    string? ApiKey);

public sealed record StoredAiSettings(
    bool Enabled,
    string BaseUrl,
    string Model,
    string? ApiKey);

public sealed record AiReviewText(
    string Headline,
    string Narrative,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Suggestions);
