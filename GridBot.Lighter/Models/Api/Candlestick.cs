using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents a single candlestick (OHLCV) data point.
/// </summary>
public sealed class Candlestick
{
    /// <summary>
    /// Unix timestamp in milliseconds for the candle open time.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// Opening price as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("open")]
    public string Open { get; set; } = "0";

    /// <summary>
    /// Highest price as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("high")]
    public string High { get; set; } = "0";

    /// <summary>
    /// Lowest price as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("low")]
    public string Low { get; set; } = "0";

    /// <summary>
    /// Closing price as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("close")]
    public string Close { get; set; } = "0";

    /// <summary>
    /// Trading volume as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("volume")]
    public string Volume { get; set; } = "0";
}

/// <summary>
/// Response wrapper for candlestick data queries.
/// </summary>
public sealed class CandlesticksResponse
{
    /// <summary>
    /// Response code. 0 or 200 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// List of candlesticks.
    /// </summary>
    [JsonPropertyName("data")]
    public List<Candlestick> Data { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
