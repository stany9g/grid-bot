using System.Net.Http.Json;
using GridBot.Web.Models;
using Microsoft.Extensions.Options;

namespace GridBot.Web.Services;

/// <summary>
/// Configuration options for the webhook notification service.
/// </summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// The webhook URL for Home Assistant notifications.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Whether webhook notifications are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds for webhook requests.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Service that sends webhook notifications to Home Assistant.
/// </summary>
public sealed class WebhookNotificationService : IWebhookNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookNotificationService> _logger;

    public WebhookNotificationService(
        HttpClient httpClient,
        IOptions<WebhookOptions> options,
        ILogger<WebhookNotificationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<bool> SendAlertAsync(AlertItem alert, CancellationToken ct = default)
    {
        var payload = WebhookPayload.FromAlert(alert);
        return await SendPayloadAsync(payload, ct);
    }

    public async Task<bool> SendPayloadAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications disabled, skipping: {EventType}", payload.EventType);
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogWarning("Webhook URL not configured, skipping notification: {EventType}", payload.EventType);
            return false;
        }

        try
        {
            _logger.LogDebug("Sending webhook notification: {EventType} - {Title}", payload.EventType, payload.Title);

            var response = await _httpClient.PostAsJsonAsync(_options.Url, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook notification sent successfully: {EventType} - {Title}",
                    payload.EventType, payload.Title);
                return true;
            }

            _logger.LogWarning("Webhook notification failed with status {StatusCode}: {EventType}",
                response.StatusCode, payload.EventType);
            return false;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Webhook notification cancelled: {EventType}", payload.EventType);
            throw;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Webhook notification timed out: {EventType}", payload.EventType);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Webhook notification failed due to network error: {EventType}", payload.EventType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending webhook notification: {EventType}", payload.EventType);
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var testPayload = new WebhookPayload
        {
            EventType = "test",
            Severity = "info",
            Title = "ALTE Bot Connection Test",
            Message = "This is a test notification from the ALTE Grid Bot dashboard.",
            Data = new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                ["source"] = "dashboard"
            }
        };

        _logger.LogInformation("Testing webhook connection to: {Url}", _options.Url);
        return await SendPayloadAsync(testPayload, ct);
    }
}
