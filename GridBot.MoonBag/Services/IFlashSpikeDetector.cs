namespace GridBot.MoonBag.Services;

/// <summary>
/// Detects flash spikes in price movements that could indicate manipulation or unstable markets.
/// Used to suspend trailing grid operations during extreme volatility.
/// </summary>
public interface IFlashSpikeDetector
{
    /// <summary>
    /// Checks if flash spike protection is currently active for a market.
    /// During active protection, high watermark updates and grid shifts are suspended.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if flash spike protection is active.</returns>
    Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a price for flash spike detection analysis.
    /// Call this periodically with current market prices.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Manually clears flash spike protection for a market.
    /// Use for operator override in exceptional situations.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ClearFlashSpikeProtection(int marketId);
}
