namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Snapshot of order book state at a point in time.
/// </summary>
public sealed class OrderBookSnapshot
{
    /// <summary>
    /// Lighter DEX market identifier.
    /// </summary>
    public int MarketId { get; init; }

    /// <summary>
    /// When this snapshot was taken.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Last traded price.
    /// </summary>
    public decimal LastPrice { get; init; }

    /// <summary>
    /// Best bid (highest buy) price.
    /// </summary>
    public decimal BestBid { get; init; }

    /// <summary>
    /// Best ask (lowest sell) price.
    /// </summary>
    public decimal BestAsk { get; init; }

    /// <summary>
    /// Bid-ask spread in absolute terms.
    /// </summary>
    public decimal Spread { get; init; }

    /// <summary>
    /// Total depth of all bid orders in USD.
    /// </summary>
    public decimal TotalBidDepth { get; init; }

    /// <summary>
    /// Total depth of all ask orders in USD.
    /// </summary>
    public decimal TotalAskDepth { get; init; }

    /// <summary>
    /// Bid price levels ordered by price descending.
    /// </summary>
    public List<PriceLevel> Bids { get; init; } = new();

    /// <summary>
    /// Ask price levels ordered by price ascending.
    /// </summary>
    public List<PriceLevel> Asks { get; init; } = new();
}

/// <summary>
/// Represents a single price level in the order book.
/// </summary>
public sealed class PriceLevel
{
    /// <summary>
    /// Price at this level.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// Total size at this price level.
    /// </summary>
    public decimal Size { get; init; }
}
