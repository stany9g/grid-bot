using System.Collections.Concurrent;
using GridBot.MoonBag.Models;
using Microsoft.Extensions.Logging;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Manages trailing stop functionality during moon bag protection.
/// Thread-safe implementation with software-managed trailing stops.
/// </summary>
public sealed class TrailingStopService : ITrailingStopService, IDisposable
{
    private readonly IMoonBagManager _moonBagManager;
    private readonly IMoonBagMarketDataProvider _marketDataProvider;
    private readonly IMoonBagOrderExecutor _orderExecutor;
    private readonly IMoonBagConfiguration _config;
    private readonly IMoonBagEventLogger _eventLogger;
    private readonly ILogger<TrailingStopService> _logger;

    private readonly ConcurrentDictionary<int, TrailingStopState> _stopStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    public TrailingStopService(
        IMoonBagManager moonBagManager,
        IMoonBagMarketDataProvider marketDataProvider,
        IMoonBagOrderExecutor orderExecutor,
        IMoonBagConfiguration config,
        IMoonBagEventLogger eventLogger,
        ILogger<TrailingStopService> logger)
    {
        ArgumentNullException.ThrowIfNull(moonBagManager);
        ArgumentNullException.ThrowIfNull(marketDataProvider);
        ArgumentNullException.ThrowIfNull(orderExecutor);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);

        _moonBagManager = moonBagManager;
        _marketDataProvider = marketDataProvider;
        _orderExecutor = orderExecutor;
        _config = config;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public async Task<(decimal StopPrice, TrailingStopTier Tier)> GetCurrentTrailingStopAsync(
        int marketId,
        CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

        if (status.State is not (MoonBagState.Trailing or MoonBagState.Triggered))
        {
            return (0m, TrailingStopTier.Standard);
        }

        var stopPrice = await CalculateTrailingStopAsync(marketId, ct).ConfigureAwait(false);
        var tier = await GetTrailingStopTierAsync(marketId, ct).ConfigureAwait(false);

        return (stopPrice, tier);
    }

    public async Task<decimal> CalculateTrailingStopAsync(int marketId, CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

        if (status.HighWatermarkPrice <= 0)
        {
            return 0m;
        }

        var tier = await GetTrailingStopTierAsync(marketId, ct).ConfigureAwait(false);

        var stopDistance = tier switch
        {
            TrailingStopTier.Emergency => _config.EmergencyStopPercent,
            TrailingStopTier.Aggressive => _config.AggressiveStopPercent,
            TrailingStopTier.Tightened => _config.TightenedStopPercent,
            _ => _config.InitialTrailingStopPercent
        };

        stopDistance = Math.Min(stopDistance, _config.MaxTrailDistance);

        decimal stopPrice;
        if (status.IsLongPosition)
        {
            stopPrice = status.HighWatermarkPrice * (1 - stopDistance);
        }
        else
        {
            stopPrice = status.HighWatermarkPrice * (1 + stopDistance);
        }

        return stopPrice;
    }

    public async Task<TrailingStopTier> GetTrailingStopTierAsync(int marketId, CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);
        var state = GetOrCreateState(marketId);

        var newTier = status.CurrentProfitPercent switch
        {
            >= 2.00m => TrailingStopTier.Emergency,
            >= 1.00m => TrailingStopTier.Aggressive,
            >= 0.50m => TrailingStopTier.Tightened,
            _ => TrailingStopTier.Standard
        };

        if (newTier > state.CurrentTier)
        {
            var oldTier = state.CurrentTier;
            state.CurrentTier = newTier;

            _logger.LogInformation(
                "Trailing stop tier tightened for market {MarketId}: {OldTier} -> {NewTier} (profit: {Profit:P1})",
                marketId, oldTier, newTier, status.CurrentProfitPercent);

            var evt = new MoonBagEvent(
                "TG-004",
                MoonBagAlertSeverity.Low,
                $"Trailing stop tightened for market {marketId}",
                $"Tier: {oldTier} -> {newTier}, profit: {status.CurrentProfitPercent:P1}",
                status.CurrentProfitPercent,
                _config.TightenAtProfitPercent50);

            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
        }

        return state.CurrentTier;
    }

    public async Task<bool> IsTrailingStopTriggeredAsync(
        int marketId,
        decimal currentPrice,
        CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

        if (status.State is not MoonBagState.Trailing)
        {
            return false;
        }

        var stopPrice = await CalculateTrailingStopAsync(marketId, ct).ConfigureAwait(false);
        if (stopPrice <= 0)
        {
            return false;
        }

        var state = GetOrCreateState(marketId);

        bool triggered;
        if (status.IsLongPosition)
        {
            triggered = currentPrice <= stopPrice;
        }
        else
        {
            triggered = currentPrice >= stopPrice;
        }

        if (triggered)
        {
            var newTickCount = state.IncrementTriggerTicks();

            if (newTickCount >= _config.TrailingStopConfirmationTicks)
            {
                _logger.LogWarning(
                    "Trailing stop confirmed for market {MarketId}: price {Price:F4} breached stop {Stop:F4} ({Ticks} confirmations)",
                    marketId, currentPrice, stopPrice, newTickCount);

                return true;
            }

            _logger.LogDebug(
                "Trailing stop tick {Tick}/{Required} for market {MarketId}: price {Price:F4} <= stop {Stop:F4}",
                newTickCount, _config.TrailingStopConfirmationTicks,
                marketId, currentPrice, stopPrice);
        }
        else
        {
            if (state.ConsecutiveTriggerTicks > 0)
            {
                _logger.LogDebug(
                    "Trailing stop trigger reset for market {MarketId}: price {Price:F4} recovered above stop {Stop:F4}",
                    marketId, currentPrice, stopPrice);
            }
            state.ResetTriggerTicks();
        }

        return false;
    }

    public async Task<bool> ExecuteTrailingStopAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

            if (status.State is not MoonBagState.Trailing)
            {
                _logger.LogWarning(
                    "Cannot execute trailing stop for market {MarketId}: not in TRAILING state (current: {State})",
                    marketId, status.State);
                return false;
            }

            var moonBagThreshold = await _moonBagManager.CalculateMoonBagThresholdAsync(marketId, ct)
                .ConfigureAwait(false);

            var currentPosition = await _orderExecutor.GetPositionSizeAsync(marketId, ct).ConfigureAwait(false);

            if (currentPosition <= 0)
            {
                _logger.LogWarning(
                    "No position found for market {MarketId}, cannot execute trailing stop",
                    marketId);

                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.Inactive,
                    "No position found on exchange").ConfigureAwait(false);

                return false;
            }

            var sellQuantity = Math.Max(0, currentPosition - moonBagThreshold);

            if (sellQuantity <= 0)
            {
                _logger.LogWarning(
                    "No quantity to sell on trailing stop for market {MarketId}: position {Position:F4} already at moon bag level {Threshold:F4}",
                    marketId, currentPosition, moonBagThreshold);

                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.HoldMode,
                    "Position already at moon bag level").ConfigureAwait(false);

                return false;
            }

            await _moonBagManager.TransitionStateAsync(
                marketId,
                MoonBagState.Triggered,
                $"Trailing stop triggered, selling {sellQuantity:F4}").ConfigureAwait(false);

            var currentPrice = await _marketDataProvider.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            var slippageAdjustedPrice = status.IsLongPosition
                ? currentPrice * 0.99m
                : currentPrice * 1.01m;

            var result = await _orderExecutor.ExecuteTrailingStopSellAsync(
                marketId,
                sellQuantity,
                slippageAdjustedPrice,
                status.IsLongPosition,
                ct).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogWarning(
                    "Trailing stop executed for market {MarketId}: sold {Quantity:F4}, tx={TxHash}",
                    marketId, sellQuantity, result.TxHash);

                var evt = new MoonBagEvent(
                    "TG-003",
                    MoonBagAlertSeverity.High,
                    $"Trailing stop executed for market {marketId}",
                    $"Sold {sellQuantity:F4}, preserved moon bag {moonBagThreshold:F4}",
                    sellQuantity,
                    moonBagThreshold);

                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);

                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.HoldMode,
                    "Non-moon-bag portion sold via trailing stop").ConfigureAwait(false);

                return true;
            }
            else
            {
                _logger.LogError(
                    "Failed to execute trailing stop for market {MarketId}: {Error}",
                    marketId, result.ErrorMessage);

                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.Trailing,
                    $"Trailing stop execution failed: {result.ErrorMessage}").ConfigureAwait(false);

                return false;
            }
        }
        finally
        {
            marketLock.Release();
        }
    }

    public async Task<bool> UpdateTrailingStopOrderAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

            if (status.State is not MoonBagState.Trailing)
            {
                return false;
            }

            var state = GetOrCreateState(marketId);

            if (state.LastStopOrderUpdate.HasValue)
            {
                var elapsed = DateTimeOffset.UtcNow - state.LastStopOrderUpdate.Value;
                if (elapsed.TotalSeconds < _config.TrailingStopUpdateIntervalSeconds)
                {
                    return false;
                }
            }

            var stopPrice = await CalculateTrailingStopAsync(marketId, ct).ConfigureAwait(false);

            if (stopPrice <= 0)
            {
                return false;
            }

            if (state.StopOrderId.HasValue)
            {
                try
                {
                    await _orderExecutor.CancelOrderAsync(marketId, state.StopOrderId.Value, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to cancel existing stop order {OrderId} for market {MarketId}",
                        state.StopOrderId, marketId);
                }
            }

            var currentPosition = await _orderExecutor.GetPositionSizeAsync(marketId, ct).ConfigureAwait(false);

            if (currentPosition <= 0)
            {
                _logger.LogDebug(
                    "No position found for market {MarketId}, cannot update trailing stop order",
                    marketId);
                return false;
            }

            var moonBagThreshold = await _moonBagManager.CalculateMoonBagThresholdAsync(marketId, ct)
                .ConfigureAwait(false);
            var sellQuantity = Math.Max(0, currentPosition - moonBagThreshold);

            if (sellQuantity <= 0)
            {
                _logger.LogDebug(
                    "Position {Position:F4} already at or below moon bag threshold {Threshold:F4} for market {MarketId}",
                    currentPosition, moonBagThreshold, marketId);
                return false;
            }

            var executionAdjustedPrice = status.IsLongPosition
                ? stopPrice * 0.99m
                : stopPrice * 1.01m;

            var result = await _orderExecutor.PlaceStopLossOrderAsync(
                marketId,
                sellQuantity,
                stopPrice,
                executionAdjustedPrice,
                status.IsLongPosition,
                ct).ConfigureAwait(false);

            if (result.Success && result.OrderId.HasValue)
            {
                state.StopOrderId = result.OrderId;
                state.LastStopOrderUpdate = DateTimeOffset.UtcNow;

                _logger.LogInformation(
                    "Trailing stop order updated for market {MarketId}: stop={Stop:F4}, quantity={Quantity:F4}, orderId={OrderId}",
                    marketId, stopPrice, sellQuantity, state.StopOrderId);

                return true;
            }
            else
            {
                _logger.LogError(
                    "Failed to place trailing stop order for market {MarketId}: {Error}",
                    marketId, result.ErrorMessage);
                return false;
            }
        }
        finally
        {
            marketLock.Release();
        }
    }

    public Task<bool> CanUpdateTrailingStopOrderAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);

        if (!state.LastStopOrderUpdate.HasValue)
        {
            return Task.FromResult(true);
        }

        var elapsed = DateTimeOffset.UtcNow - state.LastStopOrderUpdate.Value;
        return Task.FromResult(elapsed.TotalSeconds >= _config.TrailingStopUpdateIntervalSeconds);
    }

    public async Task<bool> CancelTrailingStopOrderAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        if (!state.StopOrderId.HasValue)
        {
            return true;
        }

        try
        {
            await _orderExecutor.CancelOrderAsync(marketId, state.StopOrderId.Value, ct)
                .ConfigureAwait(false);

            state.StopOrderId = null;

            _logger.LogInformation(
                "Trailing stop order cancelled for market {MarketId}",
                marketId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to cancel trailing stop order for market {MarketId}",
                marketId);
            return false;
        }
    }

    public void ResetTriggerConfirmation(int marketId)
    {
        var state = GetOrCreateState(marketId);
        state.ResetTriggerTicks();
    }

    private TrailingStopState GetOrCreateState(int marketId)
    {
        return _stopStates.GetOrAdd(marketId, _ => new TrailingStopState());
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    private sealed class TrailingStopState
    {
        public TrailingStopTier CurrentTier { get; set; } = TrailingStopTier.Standard;
        private int _consecutiveTriggerTicks;

        public int IncrementTriggerTicks() => Interlocked.Increment(ref _consecutiveTriggerTicks);
        public void ResetTriggerTicks() => Interlocked.Exchange(ref _consecutiveTriggerTicks, 0);
        public int ConsecutiveTriggerTicks => Volatile.Read(ref _consecutiveTriggerTicks);

        public long? StopOrderId { get; set; }
        public DateTimeOffset? LastStopOrderUpdate { get; set; }
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
        _stopStates.Clear();
    }
}
