using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Order book update message.
/// Channel: order_book/{MARKET_INDEX}
/// </summary>
public sealed record OrderBookMessage : WebSocketMessage
{
    /// <summary>
    /// Order book data.
    /// </summary>
    [JsonPropertyName("order_book")]
    public OrderBookData? OrderBook { get; init; }

    /// <summary>
    /// Message offset.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }
}

/// <summary>
/// Order book data structure.
/// </summary>
public sealed record OrderBookData
{
    /// <summary>
    /// Response code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Ask orders (sell side).
    /// </summary>
    [JsonPropertyName("asks")]
    public List<OrderBookLevel> Asks { get; init; } = [];

    /// <summary>
    /// Bid orders (buy side).
    /// </summary>
    [JsonPropertyName("bids")]
    public List<OrderBookLevel> Bids { get; init; } = [];

    /// <summary>
    /// Message offset.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }

    /// <summary>
    /// Nonce value.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; init; }

    /// <summary>
    /// Timestamp in milliseconds.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Order book price level.
/// </summary>
public sealed record OrderBookLevel
{
    /// <summary>
    /// Price as string for precision.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// Size as string for precision.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";
}
