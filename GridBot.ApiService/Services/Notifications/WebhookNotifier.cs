using System.Net.Http.Json;
using System.Text.Json;
using GridBot.ApiService.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Notifications;

/// <summary>
/// Sends webhook notifications to external services (e.g., Home Assistant, Discord, Telegram).
/// </summary>
public sealed class WebhookNotifier : IWebhookNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookNotifier> _logger;
    private readonly WebhookOptions _options;

    public WebhookNotifier(
        HttpClient httpClient,
        ILogger<WebhookNotifier> logger,
        IOptions<TradingBotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value.Webhook;
    }

    public async Task<bool> SendNotificationAsync(string title, string message, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications disabled, skipping");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogWarning("Webhook URL not configured, skipping notification");
            return false;
        }

        try
        {
            var payload = new
            {
                title,
                message
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _httpClient.PostAsJsonAsync(
                _options.Url,
                payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Webhook notification sent successfully: {Title}",
                    title);
                return true;
            }

            _logger.LogWarning(
                "Webhook notification failed with status {StatusCode}: {Title}",
                response.StatusCode,
                title);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Webhook notification cancelled");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Webhook notification timed out after {Timeout}s: {Title}",
                _options.TimeoutSeconds,
                title);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Webhook notification failed due to network error: {Title}",
                title);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending webhook notification: {Title}",
                title);
            return false;
        }
    }
}
