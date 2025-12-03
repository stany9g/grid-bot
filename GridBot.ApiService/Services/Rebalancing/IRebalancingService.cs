using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Rebalancing;

/// <summary>
/// Service for executing portfolio rebalancing operations.
/// </summary>
public interface IRebalancingService
{
    /// <summary>
    /// Executes a rebalancing operation based on inventory analysis.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="analysis">Inventory analysis with rebalance requirements.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the rebalancing operation.</returns>
    Task<RebalanceResult> ExecuteRebalanceAsync(int marketId, InventoryAnalysis analysis, CancellationToken ct = default);

    /// <summary>
    /// Gets the available rebalancing capacity for this hour.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Remaining rebalance capacity as percentage of portfolio.</returns>
    Task<decimal> GetAvailableRebalanceCapacityAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if rebalancing can occur right now (respects rate limits and trading state).
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>True if rebalancing is allowed.</returns>
    bool CanRebalanceNow(int marketId);
}
