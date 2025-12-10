using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridBot.Lighter;

/// <summary>
/// Shared JSON serialization options for Lighter API communication.
/// Thread-safe: JsonSerializerOptions instances are immutable after first use.
/// </summary>
public static class LighterJsonOptions
{
    /// <summary>
    /// Default JSON options for Lighter API (snake_case, ignore nulls, case-insensitive).
    /// Uses relaxed JSON escaping for better compatibility with the Lighter API.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}
