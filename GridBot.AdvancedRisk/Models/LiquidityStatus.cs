namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Current liquidity status for a market.
/// </summary>
/// <param name="MarketId">Lighter DEX market ID.</param>
/// <param name="Health">Current liquidity health classification.</param>
/// <param name="BidDepthUsd">Total bid depth in USD within the configured range.</param>
/// <param name="AskDepthUsd">Total ask depth in USD within the configured range.</param>
/// <param name="SpreadBps">Current spread in basis points.</param>
/// <param name="LastUpdated">When the status was last updated.</param>
/// <param name="RecommendedSpreadMultiplier">Recommended spread multiplier based on liquidity.</param>
/// <param name="RecommendedPositionMultiplier">Recommended position size multiplier based on liquidity.</param>
public sealed record LiquidityStatus(
    int MarketId,
    LiquidityHealth Health,
    decimal BidDepthUsd,
    decimal AskDepthUsd,
    decimal SpreadBps,
    DateTimeOffset LastUpdated,
    decimal RecommendedSpreadMultiplier,
    decimal RecommendedPositionMultiplier)
{
    /// <summary>
    /// Total order book depth in USD.
    /// </summary>
    public decimal TotalDepthUsd => BidDepthUsd + AskDepthUsd;

    /// <summary>
    /// Bid/ask imbalance ratio. 1.0 = balanced, >1.0 = more bids, less than 1.0 = more asks.
    /// </summary>
    public decimal ImbalanceRatio => AskDepthUsd > 0 ? BidDepthUsd / AskDepthUsd : 0;

    /// <summary>
    /// Whether trading should be suspended due to liquidity.
    /// </summary>
    public bool ShouldSuspendTrading => Health == LiquidityHealth.Critical;

    /// <summary>
    /// Creates an unknown liquidity status (no data available).
    /// </summary>
    public static LiquidityStatus Unknown(int marketId) => new(
        marketId,
        LiquidityHealth.Unknown,
        BidDepthUsd: 0,
        AskDepthUsd: 0,
        SpreadBps: 0,
        DateTimeOffset.UtcNow,
        RecommendedSpreadMultiplier: 2.0m,
        RecommendedPositionMultiplier: 0.5m);

    /// <summary>
    /// Creates a healthy liquidity status.
    /// </summary>
    public static LiquidityStatus Healthy(int marketId, decimal bidDepth, decimal askDepth, decimal spreadBps) => new(
        marketId,
        LiquidityHealth.Healthy,
        bidDepth,
        askDepth,
        spreadBps,
        DateTimeOffset.UtcNow,
        RecommendedSpreadMultiplier: 1.0m,
        RecommendedPositionMultiplier: 1.0m);
}
