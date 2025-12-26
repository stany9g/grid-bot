using System.Text.Json.Serialization;
using GridBot.Extended.Models.Api;

namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Order book update message from WebSocket.
/// </summary>
public sealed record OrderBookMessage : WebSocketMessageBase
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; init; }

    /// <summary>
    /// Bid updates (price, size). Size of 0 means remove level.
    /// </summary>
    [JsonPropertyName("bids")]
    public IReadOnlyList<OrderBookLevel>? Bids { get; init; }

    /// <summary>
    /// Ask updates (price, size). Size of 0 means remove level.
    /// </summary>
    [JsonPropertyName("asks")]
    public IReadOnlyList<OrderBookLevel>? Asks { get; init; }

    /// <summary>
    /// Whether this is a snapshot (full book) or delta (incremental update).
    /// </summary>
    [JsonPropertyName("isSnapshot")]
    public bool IsSnapshot { get; init; }

    /// <summary>
    /// Sequence number for ordering updates.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }
}

/// <summary>
/// Order book snapshot event for the realtime data provider.
/// </summary>
public sealed class OrderBookUpdateEvent
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Order book snapshot.
    /// </summary>
    public required OrderBookSnapshot Snapshot { get; init; }

    /// <summary>
    /// Sequence number.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// Timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Processed order book snapshot.
/// </summary>
public sealed class OrderBookSnapshot
{
    /// <summary>
    /// Best bid price.
    /// </summary>
    public decimal BestBidPrice { get; init; }

    /// <summary>
    /// Best ask price.
    /// </summary>
    public decimal BestAskPrice { get; init; }

    /// <summary>
    /// Best bid size.
    /// </summary>
    public decimal BestBidSize { get; init; }

    /// <summary>
    /// Best ask size.
    /// </summary>
    public decimal BestAskSize { get; init; }

    /// <summary>
    /// Mid price (average of best bid and ask).
    /// </summary>
    public decimal MidPrice { get; init; }

    /// <summary>
    /// Spread (best ask - best bid).
    /// </summary>
    public decimal Spread { get; init; }

    /// <summary>
    /// Spread as percentage of mid price.
    /// </summary>
    public decimal SpreadPercent { get; init; }

    /// <summary>
    /// All bid levels (price, size) sorted by price descending.
    /// </summary>
    public required IReadOnlyList<(decimal Price, decimal Size)> Bids { get; init; }

    /// <summary>
    /// All ask levels (price, size) sorted by price ascending.
    /// </summary>
    public required IReadOnlyList<(decimal Price, decimal Size)> Asks { get; init; }
}
