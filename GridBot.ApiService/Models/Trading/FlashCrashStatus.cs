namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Severity level of a flash crash event.
/// </summary>
public enum FlashCrashSeverity
{
    /// <summary>
    /// No crash detected.
    /// </summary>
    None,

    /// <summary>
    /// Minor drop (1-minute threshold breached).
    /// Pauses BUY orders for 5 minutes.
    /// </summary>
    Minor,

    /// <summary>
    /// Moderate drop (5-minute threshold breached).
    /// Pauses ALL orders for 15 minutes.
    /// </summary>
    Moderate,

    /// <summary>
    /// Severe drop (15-minute threshold breached).
    /// Cancels orders and reduces position by 50% for 1 hour.
    /// </summary>
    Severe,

    /// <summary>
    /// Extreme drop (1-hour threshold breached).
    /// Full trading halt for 4+ hours, requires manual intervention.
    /// </summary>
    Extreme
}

/// <summary>
/// Required action in response to a flash crash.
/// </summary>
public enum FlashCrashAction
{
    /// <summary>
    /// No action required.
    /// </summary>
    None,

    /// <summary>
    /// Pause all BUY orders.
    /// </summary>
    PauseBuys,

    /// <summary>
    /// Pause all orders (BUY and SELL).
    /// </summary>
    PauseAll,

    /// <summary>
    /// Cancel all orders and reduce long positions by 50%.
    /// </summary>
    CancelAndReduceHalf,

    /// <summary>
    /// Full trading halt with position reduction.
    /// </summary>
    FullHalt
}

/// <summary>
/// Represents the current flash crash detection status for a market.
/// </summary>
public sealed class FlashCrashStatus
{
    /// <summary>
    /// Whether a crash has been detected.
    /// </summary>
    public bool CrashDetected { get; init; }

    /// <summary>
    /// Severity level of the detected crash.
    /// </summary>
    public FlashCrashSeverity Severity { get; init; }

    /// <summary>
    /// Maximum price drop detected as percentage (negative value).
    /// </summary>
    public decimal DropPercent { get; init; }

    /// <summary>
    /// Timeframe over which the drop occurred.
    /// </summary>
    public TimeSpan DropTimeframe { get; init; }

    /// <summary>
    /// Required action based on severity.
    /// </summary>
    public FlashCrashAction RequiredAction { get; init; }

    /// <summary>
    /// When the protection period expires.
    /// Null if no protection is active.
    /// </summary>
    public DateTimeOffset? ProtectionUntil { get; init; }

    /// <summary>
    /// Human-readable reason for the status.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Whether currently in a protection period.
    /// </summary>
    public bool IsInProtection => ProtectionUntil.HasValue && ProtectionUntil.Value > DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a status indicating no crash detected.
    /// </summary>
    public static FlashCrashStatus NoCrash() => new()
    {
        CrashDetected = false,
        Severity = FlashCrashSeverity.None,
        DropPercent = 0m,
        DropTimeframe = TimeSpan.Zero,
        RequiredAction = FlashCrashAction.None,
        ProtectionUntil = null,
        Reason = "No flash crash detected"
    };

    /// <summary>
    /// Creates a status indicating active protection from a previous crash.
    /// </summary>
    public static FlashCrashStatus InProtection(
        FlashCrashSeverity severity,
        FlashCrashAction action,
        DateTimeOffset protectionUntil,
        string reason) => new()
    {
        CrashDetected = false,
        Severity = severity,
        DropPercent = 0m,
        DropTimeframe = TimeSpan.Zero,
        RequiredAction = action,
        ProtectionUntil = protectionUntil,
        Reason = reason
    };
}
