namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Severity level of a flash pump event.
/// </summary>
public enum FlashPumpSeverity
{
    /// <summary>
    /// No pump detected.
    /// </summary>
    None,

    /// <summary>
    /// Minor gain (1-minute threshold breached).
    /// Pauses SELL orders for 5 minutes.
    /// </summary>
    Minor,

    /// <summary>
    /// Moderate gain (5-minute threshold breached).
    /// Pauses ALL orders for 15 minutes.
    /// </summary>
    Moderate,

    /// <summary>
    /// Severe gain (15-minute threshold breached).
    /// Cancels orders and covers short position by 50% for 1 hour.
    /// </summary>
    Severe,

    /// <summary>
    /// Extreme gain (1-hour threshold breached).
    /// Full trading halt for 4+ hours, requires manual intervention.
    /// </summary>
    Extreme
}

/// <summary>
/// Required action in response to a flash pump.
/// </summary>
public enum FlashPumpAction
{
    /// <summary>
    /// No action required.
    /// </summary>
    None,

    /// <summary>
    /// Pause all SELL orders (protect short positions from being closed at loss).
    /// </summary>
    PauseSells,

    /// <summary>
    /// Pause all orders (BUY and SELL).
    /// </summary>
    PauseAll,

    /// <summary>
    /// Cancel all orders and cover (reduce) short positions by 50%.
    /// </summary>
    CancelAndCoverHalf,

    /// <summary>
    /// Full trading halt with short position reduction.
    /// </summary>
    FullHalt
}

/// <summary>
/// Represents the current flash pump detection status for a market.
/// Provides symmetric protection for SHORT positions (mirrors FlashCrashStatus for LONG positions).
/// </summary>
public sealed class FlashPumpStatus
{
    /// <summary>
    /// Whether a pump has been detected.
    /// </summary>
    public bool PumpDetected { get; init; }

    /// <summary>
    /// Severity level of the detected pump.
    /// </summary>
    public FlashPumpSeverity Severity { get; init; }

    /// <summary>
    /// Maximum price gain detected as percentage (positive value).
    /// </summary>
    public decimal GainPercent { get; init; }

    /// <summary>
    /// Timeframe over which the gain occurred.
    /// </summary>
    public TimeSpan GainTimeframe { get; init; }

    /// <summary>
    /// Required action based on severity.
    /// </summary>
    public FlashPumpAction RequiredAction { get; init; }

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
    /// Creates a status indicating no pump detected.
    /// </summary>
    public static FlashPumpStatus NoPump() => new()
    {
        PumpDetected = false,
        Severity = FlashPumpSeverity.None,
        GainPercent = 0m,
        GainTimeframe = TimeSpan.Zero,
        RequiredAction = FlashPumpAction.None,
        ProtectionUntil = null,
        Reason = "No flash pump detected"
    };

    /// <summary>
    /// Creates a status indicating active protection from a previous pump.
    /// </summary>
    public static FlashPumpStatus InProtection(
        FlashPumpSeverity severity,
        FlashPumpAction action,
        DateTimeOffset protectionUntil,
        string reason) => new()
    {
        PumpDetected = false,
        Severity = severity,
        GainPercent = 0m,
        GainTimeframe = TimeSpan.Zero,
        RequiredAction = action,
        ProtectionUntil = protectionUntil,
        Reason = reason
    };
}
