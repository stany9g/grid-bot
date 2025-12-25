using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using GridBot.AdvancedRisk.Models;
using Microsoft.Extensions.Logging;
namespace GridBot.AdvancedRisk.Services.Notifications;
/// <summary>
/// Sends notifications to external webhook targets (Discord, Telegram, etc.).
/// Thread-safe implementation with retry logic.
/// </summary>
public sealed class WebhookNotifier : IWebhookNotifier
{
    private readonly IAdvancedRiskConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookNotifier> _logger;
    private readonly ConcurrentDictionary<string, WebhookTarget> _targets = new();
    public WebhookNotifier(
        IAdvancedRiskConfiguration config,
        HttpClient httpClient,
        ILogger<WebhookNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }
    public async Task NotifyAsync(RiskEvent evt, CancellationToken ct = default)
    {
        if (!_config.WebhooksEnabled || _targets.IsEmpty)
        {
            return;
        }
        var tasks = _targets.Values
            .Where(t => t.Enabled && evt.Severity >= t.MinimumSeverity)
            .Select(t => SendToTargetAsync(t, evt, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    public void AddTarget(WebhookTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _targets[target.Name] = target;
        _logger.LogInformation("Added webhook target: {Name} ({Type})", target.Name, target.Type);
    }
    public void RemoveTarget(string name)
    {
        if (_targets.TryRemove(name, out _))
        {
            _logger.LogInformation("Removed webhook target: {Name}", name);
        }
    }
    private async Task SendToTargetAsync(WebhookTarget target, RiskEvent evt, CancellationToken ct)
    {
        var retries = 0;
        while (retries <= _config.WebhookMaxRetries)
        {
            try
            {
                var payload = BuildPayload(target, evt);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(_config.WebhookTimeoutSeconds));
                var response = await _httpClient.PostAsJsonAsync(target.Url, payload, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                _logger.LogDebug("Webhook sent to {Name}: {Code}", target.Name, evt.Code);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                retries++;
                if (retries > _config.WebhookMaxRetries)
                {
                    _logger.LogError(ex, "Failed to send webhook to {Name} after {Retries} retries", target.Name, retries);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), ct).ConfigureAwait(false);
                }
            }
        }
    }
    private static object BuildPayload(WebhookTarget target, RiskEvent evt)
    {
        return target.Type switch
        {
            WebhookType.Discord => new
            {
                content = FormatMessage(evt),
                embeds = new[]
                {
                    new
                    {
                        title = evt.Title,
                        description = evt.Description,
                        color = GetDiscordColor(evt.Severity),
                        timestamp = evt.Timestamp.ToString("o"),
                        fields = new[]
                        {
                            new { name = "Market", value = evt.MarketId.ToString(), inline = true },
                            new { name = "Severity", value = evt.Severity.ToString(), inline = true },
                            new { name = "Code", value = evt.Code, inline = true }
                        }
                    }
                }
            },
            WebhookType.Telegram => new
            {
                text = FormatMessage(evt),
                parse_mode = "HTML"
            },
            WebhookType.HomeAssistant => new
            {
                state = evt.Severity.ToString(),
                attributes = new
                {
                    code = evt.Code,
                    title = evt.Title,
                    description = evt.Description,
                    market_id = evt.MarketId,
                    timestamp = evt.Timestamp.ToString("o")
                }
            },
            _ => new
            {
                code = evt.Code,
                severity = evt.Severity.ToString(),
                marketId = evt.MarketId,
                title = evt.Title,
                description = evt.Description,
                timestamp = evt.Timestamp
            }
        };
    }
    private static string FormatMessage(RiskEvent evt)
    {
        var emoji = evt.Severity switch
        {
            RiskEventSeverity.Critical => "­čÜĘ",
            RiskEventSeverity.High => "ÔÜá´ŞĆ",
            RiskEventSeverity.Warning => "ÔÜí",
            _ => "Ôä╣´ŞĆ"
        };
        return $"{emoji} [{evt.Code}] {evt.Title}: {evt.Description} (Market {evt.MarketId})";
    }
    private static int GetDiscordColor(RiskEventSeverity severity)
    {
        return severity switch
        {
            RiskEventSeverity.Critical => 0xFF0000,
            RiskEventSeverity.High => 0xFF6600,
            RiskEventSeverity.Warning => 0xFFCC00,
            _ => 0x00CCFF
        };
    }
}
