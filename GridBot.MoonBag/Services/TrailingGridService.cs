using System.Collections.Concurrent;
using GridBot.MoonBag.Models;
using Microsoft.Extensions.Logging;

namespace GridBot.MoonBag.Services;

public sealed class TrailingGridService : ITrailingGridService, IDisposable
{
    private readonly IMoonBagGridProvider _gridProvider;
    private readonly IMoonBagMarketDataProvider _marketDataProvider;
    private readonly IFlashSpikeDetector _flashSpikeDetector;
    private readonly IMoonBagConfiguration _config;
    private readonly IMoonBagEventLogger _eventLogger;
    private readonly ILogger<TrailingGridService> _logger;

    private readonly ConcurrentDictionary<int, MarketTrailingState> _marketStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    public TrailingGridService(
        IMoonBagGridProvider gridProvider,
        IMoonBagMarketDataProvider marketDataProvider,
        IFlashSpikeDetector flashSpikeDetector,
        IMoonBagConfiguration config,
        IMoonBagEventLogger eventLogger,
        ILogger<TrailingGridService> logger)
    {
        ArgumentNullException.ThrowIfNull(gridProvider);
        ArgumentNullException.ThrowIfNull(marketDataProvider);
        ArgumentNullException.ThrowIfNull(flashSpikeDetector);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);

        _gridProvider = gridProvider;
        _marketDataProvider = marketDataProvider;
        _flashSpikeDetector = flashSpikeDetector;
        _config = config;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public async Task<bool> DetectBreakoutAsync(int marketId, CancellationToken ct = default)
    {
        var gridState = await _gridProvider.GetGridStateAsync(marketId, ct).ConfigureAwait(false);
        if (gridState is null)
        {
            return false;
        }

        var currentPrice = await _marketDataProvider.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);
        return currentPrice > gridState.UpperBound;
    }

    public async Task<GridShiftResult> ShiftGridUpwardAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var state = GetOrCreateState(marketId);

            var cooldownRemaining = GetCooldownRemaining(state);
            if (cooldownRemaining > TimeSpan.Zero)
            {
                return GridShiftResult.OnCooldown(cooldownRemaining);
            }

            if (await _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct).ConfigureAwait(false))
            {
                return GridShiftResult.FlashSpikePrevented(TimeSpan.FromMinutes(_config.FlashSpikeCooldownMinutes));
            }

            var cumulativeShift = CalculateCumulativeShift1h(state);
            if (cumulativeShift >= _config.MaxCumulativeShift1h)
            {
                _logger.LogWarning(
                    "Hourly grid shift limit reached for market {MarketId}: {Cumulative:P1}",
                    marketId, cumulativeShift);
                return GridShiftResult.HourlyLimitReached(cumulativeShift);
            }

            var gridState = await _gridProvider.GetGridStateAsync(marketId, ct).ConfigureAwait(false);
            if (gridState is null)
            {
                return new GridShiftResult
                {
                    Shifted = false,
                    Reason = "Grid not initialized"
                };
            }

            var currentPrice = await _marketDataProvider.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            if (currentPrice <= gridState.UpperBound)
            {
                return GridShiftResult.NoShiftNeeded();
            }

            var breakoutPercent = (currentPrice - gridState.UpperBound) / gridState.UpperBound;
            var steps = Math.Floor(breakoutPercent / _config.TrailingGridStep);

            if (steps < 1)
            {
                return GridShiftResult.NoShiftNeeded();
            }

            var shiftAmount = steps * _config.TrailingGridStep;

            var wasCapped = false;
            if (shiftAmount > _config.MaxShiftPercent)
            {
                _logger.LogWarning(
                    "Grid shift capped from {Original:P2} to {Capped:P2} for market {MarketId}",
                    shiftAmount, _config.MaxShiftPercent, marketId);
                shiftAmount = _config.MaxShiftPercent;
                wasCapped = true;
            }

            var currentCenter = gridState.CenterPrice;
            var gridHalfWidth = (gridState.UpperBound - gridState.LowerBound) / 2;
            var newCenter = currentCenter * (1 + shiftAmount);
            var newUpperBound = newCenter + gridHalfWidth;
            var newLowerBound = newCenter - gridHalfWidth;

            await _gridProvider.ShiftGridAsync(marketId, newCenter, ct).ConfigureAwait(false);

            lock (state.ShiftHistoryLock)
            {
                state.ShiftHistory.Add((DateTimeOffset.UtcNow, shiftAmount));
            }
            state.LastShiftTime = DateTimeOffset.UtcNow;

            CleanupShiftHistory(state);

            var newCumulativeShift = CalculateCumulativeShift1h(state);

            var evt = new MoonBagEvent(
                "TG-001",
                MoonBagAlertSeverity.Medium,
                $"Grid shifted upward for market {marketId}",
                $"Shift: {shiftAmount:P2}, New bounds: {newLowerBound:F4} - {newUpperBound:F4}",
                shiftAmount,
                _config.TrailingGridStep);

            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Grid shifted for market {MarketId}: {Shift:P2}, new bounds {Lower:F4} - {Upper:F4}",
                marketId, shiftAmount, newLowerBound, newUpperBound);

            return GridShiftResult.Success(
                newUpperBound,
                newLowerBound,
                shiftAmount,
                wasCapped,
                newCumulativeShift);
        }
        finally
        {
            marketLock.Release();
        }
    }

    public Task<TimeSpan> GetShiftCooldownRemainingAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);
        var remaining = GetCooldownRemaining(state);
        return Task.FromResult(remaining);
    }

    public Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default)
    {
        return _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct);
    }

    public Task<decimal> GetCumulativeShift1hAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);
        return Task.FromResult(CalculateCumulativeShift1h(state));
    }

    public Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        return _flashSpikeDetector.RecordPriceAsync(marketId, price, ct);
    }

    public void ClearFlashSpikeProtection(int marketId)
    {
        _flashSpikeDetector.ClearFlashSpikeProtection(marketId);
    }

    private MarketTrailingState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, id => new MarketTrailingState());
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, id => new SemaphoreSlim(1, 1));
    }

    private TimeSpan GetCooldownRemaining(MarketTrailingState state)
    {
        if (!state.LastShiftTime.HasValue)
        {
            return TimeSpan.Zero;
        }

        var cooldownEnd = state.LastShiftTime.Value.AddSeconds(_config.ShiftCooldownSeconds);
        var remaining = cooldownEnd - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static decimal CalculateCumulativeShift1h(MarketTrailingState state)
    {
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        lock (state.ShiftHistoryLock)
        {
            return state.ShiftHistory
                .Where(s => s.Timestamp >= oneHourAgo)
                .Sum(s => s.ShiftAmount);
        }
    }

    private static void CleanupShiftHistory(MarketTrailingState state)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        lock (state.ShiftHistoryLock)
        {
            state.ShiftHistory.RemoveAll(s => s.Timestamp < cutoff);
        }
    }

    private sealed class MarketTrailingState
    {
        public DateTimeOffset? LastShiftTime { get; set; }
        public List<(DateTimeOffset Timestamp, decimal ShiftAmount)> ShiftHistory { get; } = [];
        public object ShiftHistoryLock { get; } = new();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var semaphore in _marketLocks.Values)
        {
            semaphore.Dispose();
        }
        _marketLocks.Clear();
        _marketStates.Clear();
    }
}
