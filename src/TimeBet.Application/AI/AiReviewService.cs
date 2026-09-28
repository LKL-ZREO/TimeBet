using TimeBet.Application.Abstractions;
using TimeBet.Application.Reviews;

namespace TimeBet.Application.AI;

public sealed class AiReviewService
{
    private readonly DailyReviewService localReview;
    private readonly IAiSettingsRepository settingsRepository;
    private readonly IAiReviewProvider provider;

    public AiReviewService(
        DailyReviewService localReview,
        IAiSettingsRepository settingsRepository,
        IAiReviewProvider provider)
    {
        this.localReview = localReview;
        this.settingsRepository = settingsRepository;
        this.provider = provider;
    }

    public async Task<DailyReviewSnapshot> GetTodayAsync(
        CancellationToken cancellationToken = default)
    {
        var local = await localReview.GetTodayAsync(cancellationToken);
        var settings = await settingsRepository.GetAsync(cancellationToken);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return local;
        }

        try
        {
            var generated = await provider.GenerateAsync(local, settings, cancellationToken);
            return local with
            {
                Source = "ai",
                AiModel = settings.Model,
                Headline = generated.Headline,
                Narrative = generated.Narrative,
                Highlights = generated.Highlights,
                Suggestions = generated.Suggestions,
            };
        }
        catch (HttpRequestException)
        {
            return local;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return local;
        }
        catch (InvalidOperationException)
        {
            return local;
        }
    }

    public async Task<AiSettingsSnapshot> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.GetAsync(cancellationToken);
        return ToSnapshot(settings);
    }

    public async Task<AiSettingsSnapshot> UpdateSettingsAsync(
        UpdateAiSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await settingsRepository.GetAsync(cancellationToken);
        var baseUrl = NormalizeBaseUrl(request.BaseUrl);
        var model = NormalizeModel(request.Model);
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? current.ApiKey
            : request.ApiKey.Trim();
        if (request.Enabled && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("启用 AI 前请先填写 API Key。", nameof(request));
        }

        var updated = new StoredAiSettings(request.Enabled, baseUrl, model, apiKey);
        await settingsRepository.SaveAsync(updated, cancellationToken);
        return ToSnapshot(updated);
    }

    private static AiSettingsSnapshot ToSnapshot(StoredAiSettings settings)
    {
        var key = settings.ApiKey?.Trim();
        var hint = string.IsNullOrEmpty(key)
            ? null
            : key.Length <= 4 ? "已配置" : $"已配置（末尾 {key[^4..]}）";
        return new AiSettingsSnapshot(
            settings.Enabled,
            settings.BaseUrl,
            settings.Model,
            !string.IsNullOrWhiteSpace(key),
            hint);
    }

    private static string NormalizeBaseUrl(string? value)
    {
        var baseUrl = string.IsNullOrWhiteSpace(value)
            ? "https://api.openai.com/v1"
            : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("AI API 地址必须是 http 或 https URL。", nameof(value));
        }

        return baseUrl;
    }

    private static string NormalizeModel(string? value)
    {
        var model = string.IsNullOrWhiteSpace(value) ? "gpt-4o-mini" : value.Trim();
        if (model.Length > 200)
        {
            throw new ArgumentException("AI 模型名称过长。", nameof(value));
        }

        return model;
    }
}
