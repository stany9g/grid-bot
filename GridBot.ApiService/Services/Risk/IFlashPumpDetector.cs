using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Detects flash pump events and manages protection periods.
/// Provides symmetric protection for SHORT positions (mirrors IFlashCrashDetector for LONG positions).
/// Thread-safe for concurrent access.
/// </summary>
public interface IFlashPumpDetector
{
    /// <summary>
    /// Checks for flash pump conditions based on recent price history.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current flash pump status with severity and required action.</returns>
    Task<FlashPumpStatus> CheckForFlashPumpAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a price point for flash pump detection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Checks if a market is currently in pump protection mode.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>True if protection is active.</returns>
    bool IsInPumpProtection(int marketId);

    /// <summary>
    /// Gets the protection expiry time for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Protection expiry time, or null if not in protection.</returns>
    DateTimeOffset? GetProtectionExpiry(int marketId);

    /// <summary>
    /// Gets the current action required for a market in protection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Required action, or None if not in protection.</returns>
    FlashPumpAction GetCurrentAction(int marketId);

    /// <summary>
    /// Manually clears protection mode for a market. Use with caution.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ClearProtection(int marketId);

    /// <summary>
    /// Gets the count of flash pump events in the last 24 hours.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Number of pump events.</returns>
    int GetPumpCount24h(int marketId);
}
