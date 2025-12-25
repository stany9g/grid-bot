namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Detects rapid price increases (flash pumps) that may indicate manipulation.
/// Symmetric to flash crash detection for short position protection.
/// </summary>
public interface IFlashPumpDetector
{
    /// <summary>
    /// Checks if flash pump protection is currently active for a market.
    /// </summary>
    Task<bool> IsFlashPumpActiveAsync(int marketId, CancellationToken ct = default);
    /// <summary>
    /// Records a price for flash pump detection analysis.
    /// </summary>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);
    /// <summary>
    /// Manually clears flash pump protection for a market.
    /// </summary>
    void ClearFlashPumpProtection(int marketId);
}
