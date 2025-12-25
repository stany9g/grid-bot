namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Represents a risk event for structured logging and notifications.
/// </summary>
/// <param name="Code">Unique event code (e.g., "FLASH_CRASH", "LIQUIDITY_LOW").</param>
/// <param name="Severity">Event severity level.</param>
/// <param name="MarketId">Lighter DEX market ID where event occurred.</param>
/// <param name="Title">Human-readable event title.</param>
/// <param name="Description">Detailed event description.</param>
/// <param name="Timestamp">When the event occurred.</param>
/// <param name="Metadata">Additional key-value metadata for the event.</param>
public sealed record RiskEvent(
    string Code,
    RiskEventSeverity Severity,
    int MarketId,
    string Title,
    string Description,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    /// <summary>
    /// Creates a new risk event with the current timestamp.
    /// </summary>
    public static RiskEvent Create(
        string code,
        RiskEventSeverity severity,
        int marketId,
        string title,
        string description,
        Dictionary<string, object>? metadata = null)
    {
        return new RiskEvent(
            code,
            severity,
            marketId,
            title,
            description,
            DateTimeOffset.UtcNow,
            metadata);
    }

    /// <summary>
    /// Creates a flash crash detected event.
    /// </summary>
    public static RiskEvent FlashCrash(int marketId, decimal dropPercent, TimeSpan window)
    {
        return Create(
            "FLASH_CRASH",
            RiskEventSeverity.Critical,
            marketId,
            "Flash Crash Detected",
            $"Price dropped {dropPercent:P1} in {window.TotalMinutes:F0} minutes",
            new Dictionary<string, object>
            {
                ["DropPercent"] = dropPercent,
                ["WindowMinutes"] = window.TotalMinutes
            });
    }

    /// <summary>
    /// Creates a flash pump detected event.
    /// </summary>
    public static RiskEvent FlashPump(int marketId, decimal risePercent, TimeSpan window)
    {
        return Create(
            "FLASH_PUMP",
            RiskEventSeverity.Critical,
            marketId,
            "Flash Pump Detected",
            $"Price rose {risePercent:P1} in {window.TotalMinutes:F0} minutes",
            new Dictionary<string, object>
            {
                ["RisePercent"] = risePercent,
                ["WindowMinutes"] = window.TotalMinutes
            });
    }

    /// <summary>
    /// Creates a low liquidity warning event.
    /// </summary>
    public static RiskEvent LowLiquidity(int marketId, decimal bidDepth, decimal askDepth, decimal threshold)
    {
        return Create(
            "LOW_LIQUIDITY",
            RiskEventSeverity.High,
            marketId,
            "Low Liquidity Warning",
            $"Order book depth (bid: {bidDepth:F2}, ask: {askDepth:F2}) below threshold {threshold:F2}",
            new Dictionary<string, object>
            {
                ["BidDepth"] = bidDepth,
                ["AskDepth"] = askDepth,
                ["Threshold"] = threshold
            });
    }

    /// <summary>
    /// Creates a recovery phase transition event.
    /// </summary>
    public static RiskEvent RecoveryPhaseTransition(int marketId, RecoveryPhase fromPhase, RecoveryPhase toPhase)
    {
        return Create(
            "RECOVERY_TRANSITION",
            RiskEventSeverity.Info,
            marketId,
            "Recovery Phase Transition",
            $"Transitioned from {fromPhase} to {toPhase}",
            new Dictionary<string, object>
            {
                ["FromPhase"] = fromPhase.ToString(),
                ["ToPhase"] = toPhase.ToString()
            });
    }

    /// <summary>
    /// Creates an entering protective mode event.
    /// </summary>
    public static RiskEvent EnteringProtectiveMode(int marketId, string reason)
    {
        return Create(
            "PROTECTIVE_MODE_ENTER",
            RiskEventSeverity.Critical,
            marketId,
            "Entering Protective Mode",
            reason);
    }

    /// <summary>
    /// Creates an exiting protective mode event.
    /// </summary>
    public static RiskEvent ExitingProtectiveMode(int marketId)
    {
        return Create(
            "PROTECTIVE_MODE_EXIT",
            RiskEventSeverity.Info,
            marketId,
            "Exiting Protective Mode",
            "Market conditions have stabilized, beginning recovery");
    }
}
