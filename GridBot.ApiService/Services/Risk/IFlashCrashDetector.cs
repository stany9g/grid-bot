using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Detects flash crash events and manages protection periods.
/// Thread-safe for concurrent access.
/// </summary>
public interface IFlashCrashDetector
{
    /// <summary>
    /// Checks for flash crash conditions based on recent price history.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current flash crash status with severity and required action.</returns>
    Task<FlashCrashStatus> CheckForFlashCrashAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a price point for flash crash detection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Checks if a market is currently in crash protection mode.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>True if protection is active.</returns>
    bool IsInCrashProtection(int marketId);

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
    FlashCrashAction GetCurrentAction(int marketId);

    /// <summary>
    /// Manually clears protection mode for a market. Use with caution.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ClearProtection(int marketId);

    /// <summary>
    /// Gets the count of flash crash events in the last 24 hours.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Number of crash events.</returns>
    int GetCrashCount24h(int marketId);
}
