namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Candlestick (OHLCV) data for a market.
/// </summary>
public sealed record CandlestickData
{
    public DateTimeOffset Timestamp { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

/// <summary>
/// Snapshot of the order book at a point in time.
/// </summary>
public sealed record OrderBookSnapshot
{
    public int MarketId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public decimal LastPrice { get; init; }
    public decimal BestBid { get; init; }
    public decimal BestAsk { get; init; }
    public decimal Spread { get; init; }
    public decimal TotalBidDepth { get; init; }
    public decimal TotalAskDepth { get; init; }
    public List<PriceLevel> Bids { get; init; } = [];
    public List<PriceLevel> Asks { get; init; } = [];
}

/// <summary>
/// A price level in the order book.
/// </summary>
public sealed record PriceLevel
{
    public decimal Price { get; init; }
    public decimal Size { get; init; }
}
