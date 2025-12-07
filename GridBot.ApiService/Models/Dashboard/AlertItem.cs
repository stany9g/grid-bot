namespace GridBot.ApiService.Models.Dashboard;

/// <summary>
/// Severity levels for dashboard alerts.
/// </summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Represents a single alert item displayed in the dashboard alerts panel.
/// </summary>
public sealed record AlertItem
{
    /// <summary>
    /// Unique identifier for this alert.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// When the alert was created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Alert severity level.
    /// </summary>
    public required AlertSeverity Severity { get; init; }

    /// <summary>
    /// Event type for categorization.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Alert title/headline.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed alert message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Additional context data for the alert.
    /// </summary>
    public Dictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Whether the alert has been acknowledged.
    /// </summary>
    public bool IsAcknowledged { get; init; }

    /// <summary>
    /// Creates a new info-level alert.
    /// </summary>
    public static AlertItem Info(string eventType, string title, string message, Dictionary<string, object>? data = null) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Severity = AlertSeverity.Info,
        EventType = eventType,
        Title = title,
        Message = message,
        Data = data
    };

    /// <summary>
    /// Creates a new warning-level alert.
    /// </summary>
    public static AlertItem Warning(string eventType, string title, string message, Dictionary<string, object>? data = null) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Severity = AlertSeverity.Warning,
        EventType = eventType,
        Title = title,
        Message = message,
        Data = data
    };

    /// <summary>
    /// Creates a new critical-level alert.
    /// </summary>
    public static AlertItem Critical(string eventType, string title, string message, Dictionary<string, object>? data = null) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Severity = AlertSeverity.Critical,
        EventType = eventType,
        Title = title,
        Message = message,
        Data = data
    };
}

/// <summary>
/// Known alert event types.
/// </summary>
public static class AlertEventTypes
{
    public const string StateChange = "state_change";
    public const string FillDetected = "fill_detected";
    public const string FlashCrash = "flash_crash";
    public const string LossLimit = "loss_limit";
    public const string TimeoutWarning = "timeout_warning";
    public const string ProtectiveMode = "protective_mode";
    public const string RecoveryPhase = "recovery_phase";
    public const string GridShift = "grid_shift";
    public const string MoonBagTriggered = "moon_bag_triggered";
    public const string LiquidationRisk = "liquidation_risk";
}
