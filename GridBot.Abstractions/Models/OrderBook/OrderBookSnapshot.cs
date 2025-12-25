namespace GridBot.Abstractions.Models.OrderBook;

/// <summary>
/// Represents a snapshot of the order book at a specific point in time.
/// </summary>
public sealed record OrderBookSnapshot
{
    /// <summary>
    /// Gets the market identifier this order book belongs to.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the bid (buy) price levels sorted by price descending.
    /// </summary>
    public required IReadOnlyList<PriceLevel> Bids { get; init; }

    /// <summary>
    /// Gets the ask (sell) price levels sorted by price ascending.
    /// </summary>
    public required IReadOnlyList<PriceLevel> Asks { get; init; }

    /// <summary>
    /// Gets the best (highest) bid price.
    /// </summary>
    public required decimal BestBidPrice { get; init; }

    /// <summary>
    /// Gets the best (lowest) ask price.
    /// </summary>
    public required decimal BestAskPrice { get; init; }

    /// <summary>
    /// Gets the spread between best ask and best bid.
    /// </summary>
    public required decimal Spread { get; init; }

    /// <summary>
    /// Gets the timestamp when this snapshot was taken.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the mid-price calculated as (BestBid + BestAsk) / 2.
    /// </summary>
    public decimal MidPrice => (BestBidPrice + BestAskPrice) / 2m;

    /// <summary>
    /// Gets the spread as a percentage of the mid-price.
    /// </summary>
    public decimal SpreadPercent => MidPrice > 0 ? Spread / MidPrice * 100m : 0m;
}
