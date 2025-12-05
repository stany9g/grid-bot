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
    /// Opening price.
    /// </summary>
    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    /// <summary>
    /// Highest price in the period.
    /// </summary>
    [JsonPropertyName("high")]
    public decimal High { get; set; }

    /// <summary>
    /// Lowest price in the period.
    /// </summary>
    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    /// <summary>
    /// Closing price.
    /// </summary>
    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    /// <summary>
    /// Base asset trading volume.
    /// </summary>
    [JsonPropertyName("volume0")]
    public decimal Volume0 { get; set; }

    /// <summary>
    /// Quote asset trading volume.
    /// </summary>
    [JsonPropertyName("volume1")]
    public decimal Volume1 { get; set; }

    /// <summary>
    /// ID of the last trade in this candlestick period.
    /// </summary>
    [JsonPropertyName("last_trade_id")]
    public long LastTradeId { get; set; }
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
    [JsonPropertyName("candlesticks")]
    public List<Candlestick> Candlesticks { get; set; } = new();

    /// <summary>
    /// Resolution of the candlesticks (e.g., "1h", "1d").
    /// </summary>
    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
