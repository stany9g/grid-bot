using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Grid;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.Telemetry;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Manages trailing grid operations during moon bag protection.
/// Thread-safe implementation with per-market locking.
/// </summary>
public sealed class TrailingGridService : ITrailingGridService, IDisposable
{
    private readonly IGridLifecycleService _gridService;
    private readonly IMarketDataService _marketDataService;
    private readonly IFlashSpikeDetector _flashSpikeDetector;
    private readonly IRiskConfiguration _config;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ILogger<TrailingGridService> _logger;

    private readonly ConcurrentDictionary<int, MarketTrailingState> _marketStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    public TrailingGridService(
        IGridLifecycleService gridService,
        IMarketDataService marketDataService,
        IFlashSpikeDetector flashSpikeDetector,
        IRiskConfiguration config,
        IRiskEventLogger eventLogger,
        ILogger<TrailingGridService> logger)
    {
        ArgumentNullException.ThrowIfNull(gridService);
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(flashSpikeDetector);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);

        _gridService = gridService;
        _marketDataService = marketDataService;
        _flashSpikeDetector = flashSpikeDetector;
        _config = config;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> DetectBreakoutAsync(int marketId, CancellationToken ct = default)
    {
        var gridState = await _gridService.GetCurrentGridStateAsync(marketId, ct).ConfigureAwait(false);
        if (gridState?.Parameters == null)
        {
            return false;
        }

        var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);
        return currentPrice > gridState.Parameters.UpperBound;
    }

    /// <inheritdoc />
    public async Task<GridShiftResult> ShiftGridUpwardAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var state = GetOrCreateState(marketId);
            var options = _config.MoonBag;

            // Check cooldown
            var cooldownRemaining = GetCooldownRemaining(state, options);
            if (cooldownRemaining > TimeSpan.Zero)
            {
                return GridShiftResult.OnCooldown(cooldownRemaining);
            }

            // Check flash spike protection
            if (await _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct).ConfigureAwait(false))
            {
                return GridShiftResult.FlashSpikePrevented(TimeSpan.FromMinutes(_config.MoonBag.FlashSpikeCooldownMinutes));
            }

            // Check cumulative hourly shift limit
            var cumulativeShift = CalculateCumulativeShift1h(state);
            if (cumulativeShift >= options.MaxCumulativeShift1h)
            {
                _logger.LogWarning(
                    "Hourly grid shift limit reached for market {MarketId}: {Cumulative:P1}",
                    marketId, cumulativeShift);
                return GridShiftResult.HourlyLimitReached(cumulativeShift);
            }

            // Get current grid state and price
            var gridState = await _gridService.GetCurrentGridStateAsync(marketId, ct).ConfigureAwait(false);
            if (gridState?.Parameters == null)
            {
                return new GridShiftResult
                {
                    Shifted = false,
                    Reason = "Grid not initialized"
                };
            }

            var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            // Check if breakout occurred
            if (currentPrice <= gridState.Parameters.UpperBound)
            {
                return GridShiftResult.NoShiftNeeded();
            }

            // Calculate shift amount
            var breakoutPercent = (currentPrice - gridState.Parameters.UpperBound) / gridState.Parameters.UpperBound;

            // Round down to discrete steps
            var steps = Math.Floor(breakoutPercent / options.TrailingGridStep);
            if (steps < 1)
            {
                return GridShiftResult.NoShiftNeeded();
            }

            var shiftAmount = steps * options.TrailingGridStep;

            // Cap single shift
            var wasCapped = false;
            if (shiftAmount > options.MaxShiftPercent)
            {
                _logger.LogWarning(
                    "Grid shift capped from {Original:P2} to {Capped:P2} for market {MarketId}",
                    shiftAmount, options.MaxShiftPercent, marketId);
                shiftAmount = options.MaxShiftPercent;
                wasCapped = true;
            }

            // Calculate new grid bounds
            var currentCenter = gridState.Parameters.CenterPrice;
            var gridHalfWidth = (gridState.Parameters.UpperBound - gridState.Parameters.LowerBound) / 2;
            var newCenter = currentCenter * (1 + shiftAmount);
            var newUpperBound = newCenter + gridHalfWidth;
            var newLowerBound = newCenter - gridHalfWidth;

            // Execute grid shift
            await _gridService.ShiftGridAsync(marketId, newCenter, ct).ConfigureAwait(false);

            // Record grid shift metric for telemetry
            TradingMetrics.GridShifts.Add(1, TradingMetrics.MarketTag(marketId));

            // Record shift for hourly tracking
            lock (state.ShiftHistoryLock)
            {
                state.ShiftHistory.Add((DateTimeOffset.UtcNow, shiftAmount));
            }
            state.LastShiftTime = DateTimeOffset.UtcNow;

            // Clean up old shift history
            CleanupShiftHistory(state);

            // Update cumulative shift
            var newCumulativeShift = CalculateCumulativeShift1h(state);

            // Log event
            var riskEvent = RiskEvent.Create(
                "TG-001",
                AlertSeverity.Medium,
                $"Grid shifted upward for market {marketId}",
                $"Shift: {shiftAmount:P2}, New bounds: {newLowerBound:F4} - {newUpperBound:F4}",
                shiftAmount,
                options.TrailingGridStep);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

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

    /// <inheritdoc />
    public Task<TimeSpan> GetShiftCooldownRemainingAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);
        var options = _config.MoonBag;
        var remaining = GetCooldownRemaining(state, options);
        return Task.FromResult(remaining);
    }

    /// <inheritdoc />
    public Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default)
    {
        return _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct);
    }

    /// <inheritdoc />
    public Task<decimal> GetCumulativeShift1hAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);
        return Task.FromResult(CalculateCumulativeShift1h(state));
    }

    /// <inheritdoc />
    public Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        return _flashSpikeDetector.RecordPriceAsync(marketId, price, ct);
    }

    /// <inheritdoc />
    public void ClearFlashSpikeProtection(int marketId)
    {
        _flashSpikeDetector.ClearFlashSpikeProtection(marketId);
    }

    private MarketTrailingState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketTrailingState());
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    private static TimeSpan GetCooldownRemaining(MarketTrailingState state, MoonBagOptions options)
    {
        if (!state.LastShiftTime.HasValue)
        {
            return TimeSpan.Zero;
        }

        var cooldownEnd = state.LastShiftTime.Value.AddSeconds(options.ShiftCooldownSeconds);
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

    /// <summary>
    /// Internal state tracking for trailing grid per market.
    /// </summary>
    private sealed class MarketTrailingState
    {
        public DateTimeOffset? LastShiftTime { get; set; }
        public List<(DateTimeOffset Timestamp, decimal ShiftAmount)> ShiftHistory { get; } = [];
        public object ShiftHistoryLock { get; } = new();
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
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
