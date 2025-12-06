using System.Text.Json.Serialization;

namespace GridBot.Web.Models;

/// <summary>
/// Payload structure for Home Assistant webhook notifications.
/// </summary>
public sealed record WebhookPayload
{
    /// <summary>
    /// Type of event (state_change, fill_detected, flash_crash, etc.).
    /// </summary>
    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    /// <summary>
    /// Severity level: info, warning, critical.
    /// </summary>
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    /// <summary>
    /// Alert title for notifications.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Detailed message for the alert.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Additional event-specific data.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Creates a webhook payload from an alert item.
    /// </summary>
    public static WebhookPayload FromAlert(AlertItem alert) => new()
    {
        EventType = alert.EventType,
        Severity = alert.Severity.ToString().ToLowerInvariant(),
        Title = alert.Title,
        Message = alert.Message,
        Data = alert.Data
    };
}

/// <summary>
/// Known webhook event types.
/// </summary>
public static class WebhookEventTypes
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
