using System.Collections.Concurrent;
using GridBot.AdvancedRisk.Models;
using GridBot.AdvancedRisk.Services.Notifications;
using Microsoft.Extensions.Logging;
namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Logs risk events and sends notifications.
/// Thread-safe implementation with in-memory event history.
/// </summary>
public sealed class RiskEventLogger : IRiskEventLogger
{
    private readonly IWebhookNotifier _webhookNotifier;
    private readonly ILogger<RiskEventLogger> _logger;
    private readonly ConcurrentDictionary<int, ConcurrentQueue<RiskEvent>> _eventsByMarket = new();
    private const int MaxEventsPerMarket = 1000;
    public RiskEventLogger(IWebhookNotifier webhookNotifier, ILogger<RiskEventLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(webhookNotifier);
        ArgumentNullException.ThrowIfNull(logger);
        _webhookNotifier = webhookNotifier;
        _logger = logger;
    }
    public async Task LogEventAsync(RiskEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var queue = _eventsByMarket.GetOrAdd(evt.MarketId, _ => new ConcurrentQueue<RiskEvent>());
        queue.Enqueue(evt);
        while (queue.Count > MaxEventsPerMarket && queue.TryDequeue(out _)) { }
        LogToLogger(evt);
        await _webhookNotifier.NotifyAsync(evt, ct).ConfigureAwait(false);
    }
    public Task<IReadOnlyList<RiskEvent>> GetRecentEventsAsync(int marketId, int count = 50, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_eventsByMarket.TryGetValue(marketId, out var queue))
        {
            var events = queue.Reverse().Take(count).ToList();
            return Task.FromResult<IReadOnlyList<RiskEvent>>(events);
        }
        return Task.FromResult<IReadOnlyList<RiskEvent>>(Array.Empty<RiskEvent>());
    }
    private void LogToLogger(RiskEvent evt)
    {
        var logLevel = evt.Severity switch
        {
            RiskEventSeverity.Critical => LogLevel.Critical,
            RiskEventSeverity.High => LogLevel.Error,
            RiskEventSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _logger.Log(logLevel, "[{Code}] {Title}: {Description} (Market: {MarketId})",
            evt.Code, evt.Title, evt.Description, evt.MarketId);
    }
}
