using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeBet.Desktop.WebView;

internal sealed record BridgeRequest(string? Id, string? Method, JsonElement Payload);

internal sealed record BridgeError(string Code, string Message);

internal sealed record BridgeResponse(
    string Id,
    bool Ok,
    object? Result = null,
    BridgeError? Error = null);

internal sealed record BridgeEvent(string Type, object Payload);

internal static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}
