using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services.Inventory;

/// <summary>
/// Service for managing portfolio inventory and calculating rebalancing requirements.
/// </summary>
public interface IInventoryManager
{
    /// <summary>
    /// Analyzes the current inventory state and determines rebalancing needs.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inventory analysis result.</returns>
    Task<InventoryAnalysis> AnalyzeInventoryAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Calculates the target inventory skew for a given trend state.
    /// </summary>
    /// <param name="trendState">Current trend state.</param>
    /// <returns>Target crypto skew percentage (0-100).</returns>
    decimal CalculateTargetSkew(TrendState trendState);

    /// <summary>
    /// Calculates the delta needed to reach target skew from current skew.
    /// </summary>
    /// <param name="currentSkew">Current crypto allocation percentage.</param>
    /// <param name="targetSkew">Target crypto allocation percentage.</param>
    /// <returns>Delta (positive = buy crypto, negative = sell crypto).</returns>
    decimal CalculateRebalanceDelta(decimal currentSkew, decimal targetSkew);

    /// <summary>
    /// Determines whether rebalancing should occur based on threshold.
    /// </summary>
    /// <param name="currentSkew">Current crypto allocation percentage.</param>
    /// <param name="targetSkew">Target crypto allocation percentage.</param>
    /// <param name="threshold">Minimum delta to trigger rebalance (default 5%).</param>
    /// <returns>True if rebalancing should occur.</returns>
    bool ShouldRebalance(decimal currentSkew, decimal targetSkew, decimal threshold = 5m);

    /// <summary>
    /// Determines whether this is an emergency rebalance situation (delta > 30%).
    /// </summary>
    /// <param name="currentSkew">Current crypto allocation percentage.</param>
    /// <param name="targetSkew">Target crypto allocation percentage.</param>
    /// <returns>True if emergency rebalance is needed.</returns>
    bool IsEmergencyRebalance(decimal currentSkew, decimal targetSkew);
}
