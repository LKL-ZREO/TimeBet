using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TimeBet.Application.Abstractions;
using TimeBet.Application.AI;
using TimeBet.Application.Reviews;

namespace TimeBet.Infrastructure.AI;

public sealed class OpenAiCompatibleReviewProvider : IAiReviewProvider
{
    private readonly HttpClient httpClient;

    public OpenAiCompatibleReviewProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<AiReviewText> GenerateAsync(
        DailyReviewSnapshot review,
        StoredAiSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("AI API Key 未配置。");
        }

        var payload = new
        {
            model = settings.Model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "你是一个克制、客观的学习复盘助手。只根据提供的结构化事实总结，不要把软件使用推断成确定的学习成果，不要编造事实。输出严格 JSON，字段为 headline（不超过40字）、narrative（80到180字）、highlights（最多4条短句）、suggestions（最多3条短句）。中文回答。",
                },
                new
                {
                    role = "user",
                    content = BuildPrompt(review),
                },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(responseJson);
    }

    private static string BuildPrompt(DailyReviewSnapshot review)
    {
        var facts = new
        {
            date = review.Date,
            metrics = review.Metrics,
            relevance = review.Relevance,
            applications = review.Applications.Select(item => new
            {
                item.ProcessName,
                item.DurationSeconds,
                item.Share,
            }),
            sessions = review.Sessions.Select(session => new
            {
                session.Goal,
                session.Status,
                session.DurationSeconds,
                session.IdleSeconds,
                session.HighSeconds,
                session.PossibleSeconds,
                session.LowSeconds,
                session.UnknownSeconds,
            }),
            privacy = "未提供窗口标题、截图、文件内容、剪贴板、键盘输入或账号信息。",
        };
        return JsonSerializer.Serialize(facts);
    }

    private static AiReviewText ParseResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("AI 返回了空内容。");
            }

            content = StripJsonFence(content);
            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            var headline = ReadText(root, "headline", 80);
            var narrative = ReadText(root, "narrative", 600);
            var highlights = ReadList(root, "highlights", 4, 160);
            var suggestions = ReadList(root, "suggestions", 3, 200);
            if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(narrative))
            {
                throw new InvalidOperationException("AI 返回内容缺少必要字段。");
            }

            return new AiReviewText(headline, narrative, highlights, suggestions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("AI 返回内容不是有效的结构化结果。", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidOperationException("AI 返回内容缺少必要字段。", exception);
        }
    }

    private static string ReadText(JsonElement root, string property, int maxLength)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        var text = value.GetString()?.Trim() ?? string.Empty;
        return text[..Math.Min(text.Length, maxLength)];
    }

    private static string[] ReadList(JsonElement root, string property, int maxItems, int maxLength)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .OfType<string>()
            .Where(item => item.Length > 0)
            .Select(item => item[..Math.Min(item.Length, maxLength)])
            .Take(maxItems)
            .ToArray();
    }

    private static string StripJsonFence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                return trimmed[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        return trimmed;
    }
}
