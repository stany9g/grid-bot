namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Types of webhook targets.
/// </summary>
public enum WebhookType
{
    /// <summary>
    /// Discord webhook.
    /// </summary>
    Discord,

    /// <summary>
    /// Telegram bot API.
    /// </summary>
    Telegram,

    /// <summary>
    /// Home Assistant webhook.
    /// </summary>
    HomeAssistant,

    /// <summary>
    /// Generic HTTP POST webhook.
    /// </summary>
    Generic
}

/// <summary>
/// Configuration for a webhook notification target.
/// </summary>
/// <param name="Type">Type of webhook.</param>
/// <param name="Url">Webhook URL.</param>
/// <param name="Name">Display name for the target.</param>
/// <param name="MinimumSeverity">Minimum severity to notify this target.</param>
/// <param name="Enabled">Whether this target is enabled.</param>
public sealed record WebhookTarget(
    WebhookType Type,
    string Url,
    string Name,
    RiskEventSeverity MinimumSeverity = RiskEventSeverity.Warning,
    bool Enabled = true);
