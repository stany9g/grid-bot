using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Logs and retrieves risk events for monitoring and analysis.
/// Thread-safe singleton implementation with in-memory storage.
/// </summary>
public sealed class RiskEventLogger : IRiskEventLogger
{
    private readonly ILogger<RiskEventLogger> _logger;
    private readonly IWebhookNotifier _webhookNotifier;
    private readonly WebhookOptions _webhookOptions;
    private readonly ConcurrentQueue<RiskEvent> _events = new();
    private readonly object _trimLock = new();

    private const int MaxEventCount = 1000;

    /// <summary>
    /// Creates a new RiskEventLogger instance.
    /// </summary>
    public RiskEventLogger(
        ILogger<RiskEventLogger> logger,
        IWebhookNotifier webhookNotifier,
        IOptions<TradingBotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(webhookNotifier);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _webhookNotifier = webhookNotifier;
        _webhookOptions = options.Value.Webhook;
    }

    /// <inheritdoc />
    public async Task LogEventAsync(RiskEvent riskEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(riskEvent);

        _events.Enqueue(riskEvent);

        // Log to ILogger based on severity
        var logLevel = riskEvent.Severity switch
        {
            AlertSeverity.Critical => LogLevel.Critical,
            AlertSeverity.High => LogLevel.Error,
            AlertSeverity.Medium => LogLevel.Warning,
            AlertSeverity.Low => LogLevel.Information,
            _ => LogLevel.Debug
        };

        _logger.Log(
            logLevel,
            "Risk Event [{RuleId}] {Severity}: {Description}. Action: {Action}. Trigger: {TriggerValue}, Threshold: {ThresholdValue}",
            riskEvent.RuleId,
            riskEvent.Severity,
            riskEvent.Description,
            riskEvent.ActionTaken,
            riskEvent.TriggerValue,
            riskEvent.ThresholdValue);

        // Send webhook notification for events at or above minimum severity
        if (riskEvent.Severity <= _webhookOptions.MinimumSeverity)
        {
            var title = $"[{riskEvent.Severity}] {riskEvent.RuleId}";
            var message = $"{riskEvent.Description}\n\nAction: {riskEvent.ActionTaken}";

            if (riskEvent.TriggerValue.HasValue)
            {
                message += $"\nTrigger: {riskEvent.TriggerValue:F4}";
            }

            if (riskEvent.ThresholdValue.HasValue)
            {
                message += $"\nThreshold: {riskEvent.ThresholdValue:F4}";
            }

            // Fire-and-forget webhook call - don't block logging
            _ = _webhookNotifier.SendNotificationAsync(title, message, ct);
        }

        // Trim if needed
        TrimEventsIfNeeded();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RiskEvent>> GetRecentEventsAsync(int count = 100, CancellationToken ct = default)
    {
        // Take atomic snapshot for thread-safe iteration
        var snapshot = _events.ToArray();
        var events = snapshot
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<RiskEvent>>(events);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RiskEvent>> GetEventsByMarketAsync(int marketId, DateTimeOffset since, CancellationToken ct = default)
    {
        // Take atomic snapshot for thread-safe iteration
        var snapshot = _events.ToArray();
        var events = snapshot
            .Where(e => e.Timestamp >= since)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<RiskEvent>>(events);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RiskEvent>> GetEventsBySeverityAsync(AlertSeverity severity, DateTimeOffset since, CancellationToken ct = default)
    {
        // Take atomic snapshot for thread-safe iteration
        var snapshot = _events.ToArray();
        var events = snapshot
            .Where(e => e.Timestamp >= since && e.Severity <= severity) // <= because Critical < High < Medium < Low
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<RiskEvent>>(events);
    }

    /// <inheritdoc />
    public Task<Dictionary<AlertSeverity, int>> GetEventCountsBySeverityAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        // Take atomic snapshot for thread-safe iteration
        var snapshot = _events.ToArray();
        var counts = snapshot
            .Where(e => e.Timestamp >= since)
            .GroupBy(e => e.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ensure all severities are represented
        foreach (var severity in Enum.GetValues<AlertSeverity>())
        {
            counts.TryAdd(severity, 0);
        }

        return Task.FromResult(counts);
    }

    private void TrimEventsIfNeeded()
    {
        if (_events.Count <= MaxEventCount)
        {
            return;
        }

        lock (_trimLock)
        {
            // Double-check inside lock
            while (_events.Count > MaxEventCount)
            {
                _events.TryDequeue(out _);
            }
        }
    }
}
