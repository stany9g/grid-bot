using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents detailed order book data for a specific market, including bids and asks.
/// </summary>
public sealed class OrderBookDetail
{
    /// <summary>
    /// Market ID.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Market symbol (e.g., "BTC-USDC").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the order book snapshot (Unix timestamp in milliseconds).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// List of bid levels [price, size] ordered by price descending.
    /// </summary>
    [JsonPropertyName("bids")]
    public List<OrderBookLevel> Bids { get; set; } = new();

    /// <summary>
    /// List of ask levels [price, size] ordered by price ascending.
    /// </summary>
    [JsonPropertyName("asks")]
    public List<OrderBookLevel> Asks { get; set; } = new();

    /// <summary>
    /// Sequence number for the order book update.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long? Sequence { get; set; }
}

/// <summary>
/// Represents a price level in the order book (bid or ask).
/// </summary>
public sealed class OrderBookLevel
{
    /// <summary>
    /// Price level.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    /// <summary>
    /// Total size (quantity) at this price level.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    /// <summary>
    /// Number of orders at this price level.
    /// </summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }
}

/// <summary>
/// Response wrapper for order book details query.
/// </summary>
public sealed class OrderBookDetailResponse
{
    /// <summary>
    /// Response code. 0 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Order book details.
    /// </summary>
    [JsonPropertyName("data")]
    public OrderBookDetail? Data { get; set; }

    /// <summary>
    /// Gets whether the operation was successful (code == 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 0;
}
