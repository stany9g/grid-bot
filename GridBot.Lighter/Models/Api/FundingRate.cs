using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents a funding rate for a market across different exchanges.
/// </summary>
public sealed class FundingRate
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Exchange name (e.g., "lighter", "binance", "bybit").
    /// </summary>
    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// Trading symbol on the exchange.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Current funding rate as string (parse to decimal).
    /// Positive = longs pay shorts, negative = shorts pay longs.
    /// </summary>
    [JsonPropertyName("rate")]
    public string Rate { get; set; } = "0";
}

/// <summary>
/// Response wrapper for funding rates queries.
/// </summary>
public sealed class FundingRatesResponse
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
    /// List of funding rates across exchanges.
    /// </summary>
    [JsonPropertyName("data")]
    public List<FundingRate> Data { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
