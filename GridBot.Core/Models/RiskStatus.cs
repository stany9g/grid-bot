namespace GridBot.Core.Models;

/// <summary>
/// Result of a risk check.
/// </summary>
public sealed record RiskStatus
{
    /// <summary>
    /// Whether trading is safe to continue.
    /// </summary>
    public required bool IsSafe { get; init; }

    /// <summary>
    /// Reason for pause (if not safe).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Type of risk event that triggered the pause.
    /// </summary>
    public RiskEventType? EventType { get; init; }

    /// <summary>
    /// When the risk event was detected.
    /// </summary>
    public DateTimeOffset? DetectedAt { get; init; }

    /// <summary>
    /// Creates a safe status.
    /// </summary>
    public static RiskStatus Safe => new() { IsSafe = true };

    /// <summary>
    /// Creates a pause status due to flash crash.
    /// </summary>
    public static RiskStatus FlashCrash(decimal dropPercent) => new()
    {
        IsSafe = false,
        Reason = $"Flash crash detected: price dropped {dropPercent:F2}% in 1 minute",
        EventType = RiskEventType.FlashCrash,
        DetectedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a pause status due to daily loss limit.
    /// </summary>
    public static RiskStatus DailyLossLimit(decimal lossPercent, decimal limit) => new()
    {
        IsSafe = false,
        Reason = $"Daily loss limit reached: {lossPercent:F2}% (limit: {limit:F2}%)",
        EventType = RiskEventType.DailyLossLimit,
        DetectedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a pause status due to position limit.
    /// </summary>
    public static RiskStatus PositionLimit(decimal positionPercent, decimal limit) => new()
    {
        IsSafe = false,
        Reason = $"Position limit reached: {positionPercent:F2}% (limit: {limit:F2}%)",
        EventType = RiskEventType.PositionLimit,
        DetectedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a pause status with custom reason.
    /// </summary>
    public static RiskStatus Pause(string reason) => new()
    {
        IsSafe = false,
        Reason = reason,
        EventType = RiskEventType.Manual,
        DetectedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Types of risk events that can trigger a pause.
/// </summary>
public enum RiskEventType
{
    /// <summary>
    /// Manual pause requested by user.
    /// </summary>
    Manual,

    /// <summary>
    /// Flash crash detected (rapid price drop).
    /// </summary>
    FlashCrash,

    /// <summary>
    /// Daily loss limit reached.
    /// </summary>
    DailyLossLimit,

    /// <summary>
    /// Position size limit reached.
    /// </summary>
    PositionLimit
}
