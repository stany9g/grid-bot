using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Candlestick data from Extended API.
/// </summary>
public sealed record CandleResponse
{
    /// <summary>
    /// Candle open timestamp (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }

    /// <summary>
    /// Open price.
    /// </summary>
    [JsonPropertyName("open")]
    public required string Open { get; init; }

    /// <summary>
    /// High price.
    /// </summary>
    [JsonPropertyName("high")]
    public required string High { get; init; }

    /// <summary>
    /// Low price.
    /// </summary>
    [JsonPropertyName("low")]
    public required string Low { get; init; }

    /// <summary>
    /// Close price.
    /// </summary>
    [JsonPropertyName("close")]
    public required string Close { get; init; }

    /// <summary>
    /// Volume in base asset.
    /// </summary>
    [JsonPropertyName("volume")]
    public required string Volume { get; init; }

    /// <summary>
    /// Volume in quote asset.
    /// </summary>
    [JsonPropertyName("quoteVolume")]
    public string? QuoteVolume { get; init; }

    /// <summary>
    /// Number of trades in this candle.
    /// </summary>
    [JsonPropertyName("trades")]
    public int? Trades { get; init; }
}
