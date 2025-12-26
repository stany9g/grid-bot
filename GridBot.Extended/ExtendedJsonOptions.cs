using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridBot.Extended;

/// <summary>
/// Shared JSON serialization options for Extended API communication.
/// Thread-safe: JsonSerializerOptions instances are immutable after first use.
/// </summary>
public static class ExtendedJsonOptions
{
    /// <summary>
    /// Default JSON options for Extended API (camelCase, ignore nulls, case-insensitive).
    /// Extended uses camelCase for JSON properties.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
