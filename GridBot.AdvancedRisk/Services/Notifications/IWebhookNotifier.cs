using GridBot.AdvancedRisk.Models;
namespace GridBot.AdvancedRisk.Services.Notifications;
/// <summary>
/// Sends notifications to external webhook targets.
/// </summary>
public interface IWebhookNotifier
{
    /// <summary>
    /// Sends a risk event notification to all configured targets.
    /// </summary>
    Task NotifyAsync(RiskEvent evt, CancellationToken ct = default);
    /// <summary>
    /// Adds a webhook target.
    /// </summary>
    void AddTarget(WebhookTarget target);
    /// <summary>
    /// Removes a webhook target by name.
    /// </summary>
    void RemoveTarget(string name);
}
