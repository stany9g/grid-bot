using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents a trade execution on the exchange.
/// </summary>
public sealed class Trade
{
    /// <summary>
    /// Unique trade identifier.
    /// </summary>
    [JsonPropertyName("trade_id")]
    public long TradeId { get; set; }

    /// <summary>
    /// Market ID where the trade occurred.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Execution price as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    /// <summary>
    /// Trade size as string (parse to decimal for calculations).
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    /// <summary>
    /// Trade side: "buy" or "sell".
    /// </summary>
    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp in milliseconds when the trade occurred.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// Response wrapper for recent trades queries.
/// </summary>
public sealed class TradesResponse
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
    /// List of trades.
    /// </summary>
    [JsonPropertyName("data")]
    public List<Trade> Data { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
