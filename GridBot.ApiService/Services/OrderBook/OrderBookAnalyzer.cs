using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.OrderBook;

/// <summary>
/// Implementation of order book analysis.
/// Thread-safe stateless service.
/// </summary>
public sealed class OrderBookAnalyzer : IOrderBookAnalyzer
{
    /// <summary>
    /// Minimum depth threshold in USD for considering order book healthy.
    /// </summary>
    private const decimal MinimumDepthThreshold = 50_000m;

    /// <summary>
    /// Ratio threshold for severe imbalance (greater than this or inverse).
    /// </summary>
    private const decimal SevereImbalanceThreshold = 3.0m;

    /// <summary>
    /// Spread percentage threshold for wide spread warning.
    /// </summary>
    private const decimal WideSpreadThresholdPercent = 0.5m;

    /// <summary>
    /// Multiplier for detecting liquidity clusters (size > this * average).
    /// </summary>
    private const decimal LiquidityClusterMultiplier = 2.0m;

    /// <inheritdoc />
    public OrderBookAnalysis AnalyzeOrderBook(OrderBookSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var totalBidDepth = snapshot.TotalBidDepth;
        var totalAskDepth = snapshot.TotalAskDepth;

        var bidAskRatio = totalAskDepth > 0 ? totalBidDepth / totalAskDepth : 0m;

        var midPrice = (snapshot.BestBid + snapshot.BestAsk) / 2m;
        var spreadPercent = midPrice > 0 ? (snapshot.Spread / midPrice) * 100m : 0m;

        var isThinOrderBook = totalBidDepth < MinimumDepthThreshold || totalAskDepth < MinimumDepthThreshold;
        var hasSevereImbalance = bidAskRatio > SevereImbalanceThreshold ||
                                 (bidAskRatio > 0 && bidAskRatio < 1m / SevereImbalanceThreshold);
        var wideSpreadWarning = spreadPercent > WideSpreadThresholdPercent;

        return new OrderBookAnalysis
        {
            TotalBidDepth = totalBidDepth,
            TotalAskDepth = totalAskDepth,
            BidAskRatio = bidAskRatio,
            Spread = snapshot.Spread,
            SpreadPercent = spreadPercent,
            IsThinOrderBook = isThinOrderBook,
            HasSevereImbalance = hasSevereImbalance,
            WideSpreadWarning = wideSpreadWarning
        };
    }

    /// <inheritdoc />
    public decimal CalculateBidAskImbalance(OrderBookSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var totalDepth = snapshot.TotalBidDepth + snapshot.TotalAskDepth;

        if (totalDepth <= 0)
            return 0m;

        // Returns value between -1 and 1
        // Positive = more bids (buying pressure)
        // Negative = more asks (selling pressure)
        return (snapshot.TotalBidDepth - snapshot.TotalAskDepth) / totalDepth;
    }

    /// <inheritdoc />
    public List<LiquidityCluster> DetectLiquidityClusters(OrderBookSnapshot snapshot, decimal averageDepth)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var clusters = new List<LiquidityCluster>();
        var threshold = averageDepth * LiquidityClusterMultiplier;

        if (threshold <= 0)
        {
            // Calculate average from snapshot if no average provided
            var allLevels = snapshot.Bids.Count + snapshot.Asks.Count;
            if (allLevels > 0)
            {
                var totalValue = snapshot.Bids.Sum(b => b.Price * b.Size) +
                                 snapshot.Asks.Sum(a => a.Price * a.Size);
                averageDepth = totalValue / allLevels;
                threshold = averageDepth * LiquidityClusterMultiplier;
            }
        }

        if (threshold <= 0)
            return clusters;

        // Check bid levels for clusters
        foreach (var bid in snapshot.Bids)
        {
            var levelValue = bid.Price * bid.Size;
            if (levelValue > threshold)
            {
                clusters.Add(new LiquidityCluster
                {
                    Price = bid.Price,
                    Size = bid.Size,
                    IsBid = true
                });
            }
        }

        // Check ask levels for clusters
        foreach (var ask in snapshot.Asks)
        {
            var levelValue = ask.Price * ask.Size;
            if (levelValue > threshold)
            {
                clusters.Add(new LiquidityCluster
                {
                    Price = ask.Price,
                    Size = ask.Size,
                    IsBid = false
                });
            }
        }

        return clusters;
    }
}
