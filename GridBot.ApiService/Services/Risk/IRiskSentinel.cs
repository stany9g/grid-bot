using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Orchestrates all risk monitoring components and coordinates responses.
/// Thread-safe for concurrent access.
/// </summary>
public interface IRiskSentinel
{
    /// <summary>
    /// Performs a comprehensive risk assessment for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated risk assessment.</returns>
    Task<RiskAssessment> AssessRiskAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Quick check if trading is currently allowed for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if trading is allowed.</returns>
    Task<bool> IsTradingAllowedAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Handles a risk event by taking appropriate action.
    /// </summary>
    /// <param name="riskEvent">The risk event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleRiskEventAsync(RiskEvent riskEvent, CancellationToken ct = default);

    /// <summary>
    /// Records a price update for risk monitoring.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordPriceUpdateAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Records an equity update for loss monitoring.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="equity">Current equity value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordEquityUpdateAsync(int marketId, decimal equity, CancellationToken ct = default);

    /// <summary>
    /// Records a trade result for P&amp;L tracking.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="pnlPercent">P&amp;L as percentage of equity.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default);

    /// <summary>
    /// Gets the recommended position size multiplier based on risk.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Position multiplier (0.0 to 1.0).</returns>
    decimal GetPositionSizeMultiplier(int marketId);

    /// <summary>
    /// Gets the recommended spread multiplier based on liquidity.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Spread multiplier (1.0+).</returns>
    decimal GetSpreadMultiplier(int marketId);
}
