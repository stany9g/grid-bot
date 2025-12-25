using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Detects flash spikes (rapid price increases) that may indicate manipulation.
/// Thread-safe implementation with per-market state tracking.
/// </summary>
public sealed class FlashSpikeDetector : IFlashSpikeDetector, IDisposable
{
    private readonly IMoonBagConfiguration _config;
    private readonly ILogger<FlashSpikeDetector> _logger;

    private readonly ConcurrentDictionary<int, MarketFlashSpikeState> _marketStates = new();
    private bool _disposed;

    /// <summary>
    /// Flash spike detection window in minutes.
    /// </summary>
    private const int FlashSpikeWindowMinutes = 5;

    /// <summary>
    /// Price history retention in minutes.
    /// </summary>
    private const int PriceHistoryMinutes = 10;

    public FlashSpikeDetector(
        IMoonBagConfiguration config,
        ILogger<FlashSpikeDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var state = GetOrCreateState(marketId);

        if (state.FlashSpikeUntil.HasValue && DateTimeOffset.UtcNow < state.FlashSpikeUntil.Value)
        {
            return Task.FromResult(true);
        }

        // Clear expired protection
        if (state.FlashSpikeUntil.HasValue)
        {
            state.FlashSpikeUntil = null;
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var state = GetOrCreateState(marketId);
        var now = DateTimeOffset.UtcNow;

        lock (state.PriceHistoryLock)
        {
            state.PriceHistory.Add((now, price));

            // Trim old entries
            var cutoff = now.AddMinutes(-PriceHistoryMinutes);
            state.PriceHistory.RemoveAll(p => p.Timestamp < cutoff);
        }

        // Check for flash spike
        DetectFlashSpike(state, marketId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void ClearFlashSpikeProtection(int marketId)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            state.FlashSpikeUntil = null;
            _logger.LogWarning("Flash spike protection manually cleared for market {MarketId}", marketId);
        }
    }

    private MarketFlashSpikeState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketFlashSpikeState());
    }

    private void DetectFlashSpike(MarketFlashSpikeState state, int marketId)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddMinutes(-FlashSpikeWindowMinutes);

        List<(DateTimeOffset Timestamp, decimal Price)> windowPrices;
        lock (state.PriceHistoryLock)
        {
            windowPrices = state.PriceHistory
                .Where(p => p.Timestamp >= windowStart)
                .ToList();
        }

        if (windowPrices.Count < 2)
        {
            return;
        }

        var minPrice = windowPrices.Min(p => p.Price);
        var currentPrice = windowPrices.Last().Price;

        // Check for upward spike (price increased significantly)
        if (minPrice > 0)
        {
            var increase = (currentPrice - minPrice) / minPrice;
            if (increase >= _config.FlashSpikeThreshold)
            {
                state.FlashSpikeUntil = now.AddMinutes(_config.FlashSpikeCooldownMinutes);
                _logger.LogWarning(
                    "Flash spike detected for market {MarketId}: {Increase:P1} increase in {Window} minutes. Protection active until {Until}",
                    marketId, increase, FlashSpikeWindowMinutes, state.FlashSpikeUntil);
            }
        }
    }

    /// <summary>
    /// Internal state for flash spike detection per market.
    /// </summary>
    private sealed class MarketFlashSpikeState
    {
        public DateTimeOffset? FlashSpikeUntil { get; set; }
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
