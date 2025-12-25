using System.Collections.Concurrent;
using GridBot.AdvancedRisk.Models;
using Microsoft.Extensions.Logging;
namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Detects flash pumps (rapid price increases) that may indicate manipulation.
/// Thread-safe implementation with per-market state tracking.
/// </summary>
public sealed class FlashPumpDetector : IFlashPumpDetector, IDisposable
{
    private readonly IAdvancedRiskConfiguration _config;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ILogger<FlashPumpDetector> _logger;
    private readonly ConcurrentDictionary<int, MarketFlashPumpState> _marketStates = new();
    private bool _disposed;
    public FlashPumpDetector(
        IAdvancedRiskConfiguration config,
        IRiskEventLogger eventLogger,
        ILogger<FlashPumpDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _eventLogger = eventLogger;
        _logger = logger;
    }
    public Task<bool> IsFlashPumpActiveAsync(int marketId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = GetOrCreateState(marketId);
        if (state.FlashPumpUntil.HasValue && DateTimeOffset.UtcNow < state.FlashPumpUntil.Value)
        {
            return Task.FromResult(true);
        }
        if (state.FlashPumpUntil.HasValue)
        {
            state.FlashPumpUntil = null;
        }
        return Task.FromResult(false);
    }
    public async Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = GetOrCreateState(marketId);
        var now = DateTimeOffset.UtcNow;
        lock (state.PriceHistoryLock)
        {
            state.PriceHistory.Add((now, price));
            var cutoff = now.AddMinutes(-_config.FlashPumpWindowMinutes * 2);
            state.PriceHistory.RemoveAll(p => p.Timestamp < cutoff);
        }
        await DetectFlashPumpAsync(state, marketId, ct).ConfigureAwait(false);
    }
    public void ClearFlashPumpProtection(int marketId)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            state.FlashPumpUntil = null;
            _logger.LogWarning("Flash pump protection manually cleared for market {MarketId}", marketId);
        }
    }
    private MarketFlashPumpState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketFlashPumpState());
    }
    private async Task DetectFlashPumpAsync(MarketFlashPumpState state, int marketId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddMinutes(-_config.FlashPumpWindowMinutes);
        List<(DateTimeOffset Timestamp, decimal Price)> windowPrices;
        lock (state.PriceHistoryLock)
        {
            windowPrices = state.PriceHistory.Where(p => p.Timestamp >= windowStart).ToList();
        }
        if (windowPrices.Count < 2) return;
        var minPrice = windowPrices.Min(p => p.Price);
        var currentPrice = windowPrices.Last().Price;
        if (minPrice > 0)
        {
            var increase = (currentPrice - minPrice) / minPrice;
            if (increase >= _config.FlashPumpThreshold)
            {
                state.FlashPumpUntil = now.AddMinutes(_config.FlashPumpCooldownMinutes);
                _logger.LogWarning(
                    "Flash pump detected for market {MarketId}: {Increase:P1} increase in {Window} minutes",
                    marketId, increase, _config.FlashPumpWindowMinutes);
                var evt = RiskEvent.FlashPump(marketId, increase, TimeSpan.FromMinutes(_config.FlashPumpWindowMinutes));
                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
            }
        }
    }
    private sealed class MarketFlashPumpState
    {
        public DateTimeOffset? FlashPumpUntil { get; set; }
        public List<(DateTimeOffset Timestamp, decimal Price)> PriceHistory { get; } = [];
        public object PriceHistoryLock { get; } = new();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _marketStates.Clear();
    }
}
