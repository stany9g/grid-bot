namespace GridBot.ApiService.Services.Notifications;

/// <summary>
/// Sends webhook notifications for critical alerts.
/// </summary>
public interface IWebhookNotifier
{
    /// <summary>
    /// Sends a notification to the configured webhook endpoint.
    /// </summary>
    /// <param name="title">Alert title.</param>
    /// <param name="message">Alert message body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if notification was sent successfully.</returns>
    Task<bool> SendNotificationAsync(string title, string message, CancellationToken ct = default);
}
