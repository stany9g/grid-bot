using GridBot.Web.Models;

namespace GridBot.Web.Services;

/// <summary>
/// Service for sending webhook notifications to Home Assistant.
/// </summary>
public interface IWebhookNotificationService
{
    /// <summary>
    /// Sends an alert to the configured webhook endpoint.
    /// </summary>
    /// <param name="alert">The alert to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the alert was sent successfully.</returns>
    Task<bool> SendAlertAsync(AlertItem alert, CancellationToken ct = default);

    /// <summary>
    /// Sends a raw webhook payload to the configured endpoint.
    /// </summary>
    /// <param name="payload">The payload to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the payload was sent successfully.</returns>
    Task<bool> SendPayloadAsync(WebhookPayload payload, CancellationToken ct = default);

    /// <summary>
    /// Tests the webhook connection by sending a test payload.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the connection test was successful.</returns>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
