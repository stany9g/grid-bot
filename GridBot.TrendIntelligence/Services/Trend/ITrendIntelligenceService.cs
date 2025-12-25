using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services.Trend;

/// <summary>
/// Orchestrator service for trend intelligence processing cycle.
/// Coordinates trend detection, inventory management, and rebalancing.
/// </summary>
public interface ITrendIntelligenceService
{
    /// <summary>
    /// Processes a complete trend intelligence cycle for a market.
    /// Analyzes trend, updates state if confirmed, analyzes inventory,
    /// and executes rebalancing if needed.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the trend intelligence cycle.</returns>
    Task<TrendIntelligenceResult> ProcessTrendCycleAsync(int marketId, CancellationToken ct = default);
}
