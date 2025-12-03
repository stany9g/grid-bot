namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Analysis results from order book examination.
/// </summary>
public sealed class OrderBookAnalysis
{
    /// <summary>
    /// Total depth on the bid side in USD.
    /// </summary>
    public decimal TotalBidDepth { get; init; }

    /// <summary>
    /// Total depth on the ask side in USD.
    /// </summary>
    public decimal TotalAskDepth { get; init; }

    /// <summary>
    /// Ratio of bid to ask depth.
    /// Greater than 1 indicates more buy-side liquidity.
    /// </summary>
    public decimal BidAskRatio { get; init; }

    /// <summary>
    /// Absolute spread between best bid and best ask.
    /// </summary>
    public decimal Spread { get; init; }

    /// <summary>
    /// Spread as percentage of mid price.
    /// </summary>
    public decimal SpreadPercent { get; init; }

    /// <summary>
    /// True if order book depth is below minimum threshold ($50,000).
    /// </summary>
    public bool IsThinOrderBook { get; init; }

    /// <summary>
    /// True if bid/ask ratio indicates severe imbalance (ratio greater than 3.0 or less than 0.33).
    /// </summary>
    public bool HasSevereImbalance { get; init; }

    /// <summary>
    /// True if spread exceeds 0.5% threshold.
    /// </summary>
    public bool WideSpreadWarning { get; init; }
}

/// <summary>
/// Represents a detected liquidity cluster in the order book.
/// </summary>
public sealed class LiquidityCluster
{
    /// <summary>
    /// Price level of the cluster.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// Total size at this cluster.
    /// </summary>
    public decimal Size { get; init; }

    /// <summary>
    /// True if this is a bid (buy) cluster, false if ask (sell).
    /// </summary>
    public bool IsBid { get; init; }
}
