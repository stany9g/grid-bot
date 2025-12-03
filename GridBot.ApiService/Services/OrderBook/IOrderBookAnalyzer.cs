using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.OrderBook;

/// <summary>
/// Service for analyzing order book state and detecting significant patterns.
/// </summary>
public interface IOrderBookAnalyzer
{
    /// <summary>
    /// Performs comprehensive analysis of an order book snapshot.
    /// </summary>
    /// <param name="snapshot">Order book snapshot to analyze.</param>
    /// <returns>Analysis results with warnings and metrics.</returns>
    OrderBookAnalysis AnalyzeOrderBook(OrderBookSnapshot snapshot);

    /// <summary>
    /// Calculates the bid-ask imbalance ratio.
    /// </summary>
    /// <param name="snapshot">Order book snapshot.</param>
    /// <returns>Imbalance ratio (positive = more bids, negative = more asks).</returns>
    decimal CalculateBidAskImbalance(OrderBookSnapshot snapshot);

    /// <summary>
    /// Detects liquidity clusters (price levels with unusually high depth).
    /// </summary>
    /// <param name="snapshot">Order book snapshot.</param>
    /// <param name="averageDepth">Average depth per level for comparison.</param>
    /// <returns>List of detected liquidity clusters.</returns>
    List<LiquidityCluster> DetectLiquidityClusters(OrderBookSnapshot snapshot, decimal averageDepth);
}
