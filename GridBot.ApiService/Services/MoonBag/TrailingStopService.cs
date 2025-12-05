using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Risk;
using GridBot.Lighter;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Manages trailing stop functionality during moon bag protection.
/// Thread-safe implementation with software-managed trailing stops.
/// </summary>
/// <remarks>
/// Lighter DEX does not have native trailing stops, so this service:
/// - Manages stop-loss limit orders that simulate trailing stop behavior
/// - Updates stop orders when high watermark increases
/// - Handles tiered tightening based on profit percentage
/// - Executes stop when price drops to trailing stop level
/// </remarks>
public sealed class TrailingStopService : ITrailingStopService, IDisposable
{
    private readonly IMoonBagManager _moonBagManager;
    private readonly IMarketDataService _marketDataService;
    private readonly IMarketScalingService _scalingService;
    private readonly ILighterCommandClient _commandClient;
    private readonly ILighterQueryClient _queryClient;
    private readonly IRiskConfiguration _config;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ILogger<TrailingStopService> _logger;

    /// <summary>
    /// Account index for trading operations.
    /// In a production system, this would come from configuration.
    /// </summary>
    private const long AccountIndex = 0;

    private readonly ConcurrentDictionary<int, TrailingStopState> _stopStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    public TrailingStopService(
        IMoonBagManager moonBagManager,
        IMarketDataService marketDataService,
        IMarketScalingService scalingService,
        ILighterCommandClient commandClient,
        ILighterQueryClient queryClient,
        IRiskConfiguration config,
        IRiskEventLogger eventLogger,
        ILogger<TrailingStopService> logger)
    {
        ArgumentNullException.ThrowIfNull(moonBagManager);
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(scalingService);
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);

        _moonBagManager = moonBagManager;
        _marketDataService = marketDataService;
        _scalingService = scalingService;
        _commandClient = commandClient;
        _queryClient = queryClient;
        _config = config;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<decimal> CalculateTrailingStopAsync(int marketId, CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

        if (status.HighWatermarkPrice <= 0)
        {
            return 0m;
        }

        var options = _config.MoonBag;
        var tier = await GetTrailingStopTierAsync(marketId, ct).ConfigureAwait(false);

        // Get stop distance based on tier (one-way tightening)
        var stopDistance = tier switch
        {
            TrailingStopTier.Emergency => options.EmergencyStopPercent,
            TrailingStopTier.Aggressive => options.AggressiveStopPercent,
            TrailingStopTier.Tightened => options.TightenedStopPercent,
            _ => options.InitialTrailingStopPercent
        };

        // Enforce maximum trail distance
        stopDistance = Math.Min(stopDistance, options.MaxTrailDistance);

        // Calculate stop price
        decimal stopPrice;
        if (status.IsLongPosition)
        {
            // For longs, stop is below high watermark
            stopPrice = status.HighWatermarkPrice * (1 - stopDistance);
        }
        else
        {
            // For shorts (inverse), stop is above low watermark
            stopPrice = status.HighWatermarkPrice * (1 + stopDistance);
        }

        return stopPrice;
    }

    /// <inheritdoc />
    public async Task<TrailingStopTier> GetTrailingStopTierAsync(int marketId, CancellationToken ct = default)
    {
        var status = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);
        var options = _config.MoonBag;

        // Get current tier from state (one-way tightening)
        var state = GetOrCreateState(marketId);

        // Determine tier based on profit
        var newTier = status.CurrentProfitPercent switch
        {
            >= 2.00m => TrailingStopTier.Emergency,  // >200% profit
            >= 1.00m => TrailingStopTier.Aggressive, // >100% profit
            >= 0.50m => TrailingStopTier.Tightened,  // >50% profit
            _ => TrailingStopTier.Standard
        };

        // One-way tightening - only update if tighter
        if (newTier > state.CurrentTier)
        {
            // HIGH-003 FIX: Capture old tier before updating
            var oldTier = state.CurrentTier;
            state.CurrentTier = newTier;

            _logger.LogInformation(
                "Trailing stop tier tightened for market {MarketId}: {OldTier} -> {NewTier} (profit: {Profit:P1})",
                marketId, oldTier, newTier, status.CurrentProfitPercent);

            // Log event
            var riskEvent = RiskEvent.Create(
                "TG-004",
                AlertSeverity.Low,
                $"Trailing stop tightened for market {marketId}",
                $"Tier: {oldTier} -> {newTier}, profit: {status.CurrentProfitPercent:P1}",
                status.CurrentProfitPercent,
                options.TightenAtProfitPercent50);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
        }

        return state.CurrentTier;
    }

    /// <inheritdoc />
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
        var options = _config.MoonBag;

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

            if (newTickCount >= options.TrailingStopConfirmationTicks)
            {
                _logger.LogWarning(
                    "Trailing stop confirmed for market {MarketId}: price {Price:F4} breached stop {Stop:F4} ({Ticks} confirmations)",
                    marketId, currentPrice, stopPrice, newTickCount);

                return true;
            }

            _logger.LogDebug(
                "Trailing stop tick {Tick}/{Required} for market {MarketId}: price {Price:F4} <= stop {Stop:F4}",
                newTickCount, options.TrailingStopConfirmationTicks,
                marketId, currentPrice, stopPrice);
        }
        else
        {
            // Price recovered, reset counter
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

    /// <inheritdoc />
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

            var options = _config.MoonBag;

            // Calculate sell quantity (85% of position, keep 15% moon bag)
            var moonBagThreshold = await _moonBagManager.CalculateMoonBagThresholdAsync(marketId, ct)
                .ConfigureAwait(false);

            // CRITICAL FIX: Get actual current position from exchange, not stale max position
            var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);

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

            // Transition to TRIGGERED state
            await _moonBagManager.TransitionStateAsync(
                marketId,
                MoonBagState.Triggered,
                $"Trailing stop triggered, selling {sellQuantity:F4}").ConfigureAwait(false);

            // Get current price for order
            var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            // Create market sell order for non-moon-bag portion
            // Note: Using aggressive limit with 1% slippage tolerance
            var slippageAdjustedPrice = status.IsLongPosition
                ? currentPrice * 0.99m  // 1% below for sells
                : currentPrice * 1.01m; // 1% above for shorts

            // Scale using market-specific decimals from metadata
            var slippagePrice = await _scalingService.ScalePriceAsync(slippageAdjustedPrice, marketId, ct).ConfigureAwait(false);
            var scaledSellQuantity = await _scalingService.ScaleBaseAmountAsync(sellQuantity, marketId, ct).ConfigureAwait(false);

            var sellOrder = new CreateOrderRequest
            {
                MarketIndex = marketId,
                ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                BaseAmount = scaledSellQuantity,
                Price = slippagePrice,
                IsAsk = status.IsLongPosition, // Sell for long, buy for short
                OrderType = OrderType.Limit,
                TimeInForce = TimeInForce.ImmediateOrCancel,
                ReduceOnly = true
            };

            try
            {
                var response = await _commandClient.CreateOrderAsync(sellOrder, true, ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Trailing stop executed for market {MarketId}: sold {Quantity:F4}, tx={TxHash}",
                    marketId, sellQuantity, response.TxHash);

                // Log event
                var riskEvent = RiskEvent.Create(
                    "TG-003",
                    AlertSeverity.High,
                    $"Trailing stop executed for market {marketId}",
                    $"Sold {sellQuantity:F4}, preserved moon bag {moonBagThreshold:F4}",
                    sellQuantity,
                    moonBagThreshold);

                await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

                // Transition to HOLD_MODE
                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.HoldMode,
                    "Non-moon-bag portion sold via trailing stop").ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to execute trailing stop for market {MarketId}",
                    marketId);

                // Revert to TRAILING state for retry
                await _moonBagManager.TransitionStateAsync(
                    marketId,
                    MoonBagState.Trailing,
                    $"Trailing stop execution failed: {ex.Message}").ConfigureAwait(false);

                return false;
            }
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
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
            var options = _config.MoonBag;

            // Check cooldown
            if (state.LastStopOrderUpdate.HasValue)
            {
                var elapsed = DateTimeOffset.UtcNow - state.LastStopOrderUpdate.Value;
                if (elapsed.TotalSeconds < options.TrailingStopUpdateIntervalSeconds)
                {
                    return false;
                }
            }

            var stopPrice = await CalculateTrailingStopAsync(marketId, ct).ConfigureAwait(false);

            if (stopPrice <= 0)
            {
                return false;
            }

            // Cancel existing stop order if any
            if (state.StopOrderId.HasValue)
            {
                try
                {
                    await _commandClient.CancelOrderAsync(marketId, state.StopOrderId.Value, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to cancel existing stop order {OrderId} for market {MarketId}",
                        state.StopOrderId, marketId);
                }
            }

            // CRITICAL FIX: Get actual current position from exchange, not stale max position
            var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);

            if (currentPosition <= 0)
            {
                _logger.LogDebug(
                    "No position found for market {MarketId}, cannot update trailing stop order",
                    marketId);
                return false;
            }

            // Calculate quantity for stop order based on actual position
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

            // Create new stop-loss order
            // Using StopLossLimit with aggressive price for execution
            var executionAdjustedPrice = status.IsLongPosition
                ? stopPrice * 0.99m   // 1% below stop for slippage
                : stopPrice * 1.01m;  // 1% above for shorts

            // Scale using market-specific decimals from metadata
            var executionPrice = await _scalingService.ScalePriceAsync(executionAdjustedPrice, marketId, ct).ConfigureAwait(false);
            var triggerPrice = await _scalingService.ScalePriceAsync(stopPrice, marketId, ct).ConfigureAwait(false);
            var scaledSellQuantity = await _scalingService.ScaleBaseAmountAsync(sellQuantity, marketId, ct).ConfigureAwait(false);

            // Use timestamp as client order index - this will be our order identifier
            var clientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var stopOrder = new CreateOrderRequest
            {
                MarketIndex = marketId,
                ClientOrderIndex = clientOrderIndex,
                BaseAmount = scaledSellQuantity,
                Price = executionPrice,
                IsAsk = status.IsLongPosition,
                OrderType = OrderType.StopLossLimit,
                TimeInForce = TimeInForce.GoodTillTime,
                ReduceOnly = true,
                TriggerPrice = (int)triggerPrice
            };

            try
            {
                var response = await _commandClient.CreateOrderAsync(stopOrder, false, ct)
                    .ConfigureAwait(false);

                // Store the client order index as our order identifier for later cancellation
                state.StopOrderId = clientOrderIndex;
                state.LastStopOrderUpdate = DateTimeOffset.UtcNow;

                _logger.LogInformation(
                    "Trailing stop order updated for market {MarketId}: stop={Stop:F4}, quantity={Quantity:F4}, orderId={OrderId}",
                    marketId, stopPrice, sellQuantity, clientOrderIndex);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to place trailing stop order for market {MarketId}",
                    marketId);
                return false;
            }
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> CanUpdateTrailingStopOrderAsync(int marketId)
    {
        var state = GetOrCreateState(marketId);
        var options = _config.MoonBag;

        if (!state.LastStopOrderUpdate.HasValue)
        {
            return Task.FromResult(true);
        }

        var elapsed = DateTimeOffset.UtcNow - state.LastStopOrderUpdate.Value;
        return Task.FromResult(elapsed.TotalSeconds >= options.TrailingStopUpdateIntervalSeconds);
    }

    /// <inheritdoc />
    public async Task<bool> CancelTrailingStopOrderAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        if (!state.StopOrderId.HasValue)
        {
            return true;
        }

        try
        {
            await _commandClient.CancelOrderAsync(marketId, state.StopOrderId.Value, ct)
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

    /// <inheritdoc />
    public void ResetTriggerConfirmation(int marketId)
    {
        var state = GetOrCreateState(marketId);
        state.ResetTriggerTicks();
    }

    /// <summary>
    /// Fetches the current position size from the exchange for the specified market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current position size (absolute value), or 0 if no position exists.</returns>
    private async Task<decimal> GetCurrentPositionAsync(int marketId, CancellationToken ct)
    {
        try
        {
            var account = await _queryClient.GetAccountAsync(AccountIndex, ct).ConfigureAwait(false);

            if (account.Positions == null || account.Positions.Count == 0)
            {
                return 0m;
            }

            var position = account.Positions.FirstOrDefault(p => p.MarketId == marketId);
            if (position == null)
            {
                return 0m;
            }

            // Parse position size - returns absolute value since sign indicates direction
            if (!decimal.TryParse(position.Size, out var size))
            {
                _logger.LogWarning(
                    "Failed to parse position size '{Size}' for market {MarketId}",
                    position.Size, marketId);
                return 0m;
            }

            // Return absolute value - direction is tracked separately in moon bag status
            return Math.Abs(size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch current position for market {MarketId}",
                marketId);
            return 0m;
        }
    }

    private TrailingStopState GetOrCreateState(int marketId)
    {
        return _stopStates.GetOrAdd(marketId, _ => new TrailingStopState());
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Internal state for trailing stop management per market.
    /// </summary>
    private sealed class TrailingStopState
    {
        public TrailingStopTier CurrentTier { get; set; } = TrailingStopTier.Standard;
        private int _consecutiveTriggerTicks;

        /// <summary>
        /// Atomically increments the consecutive trigger ticks counter.
        /// </summary>
        /// <returns>The new value after incrementing.</returns>
        public int IncrementTriggerTicks() => Interlocked.Increment(ref _consecutiveTriggerTicks);

        /// <summary>
        /// Atomically resets the consecutive trigger ticks counter to zero.
        /// </summary>
        public void ResetTriggerTicks() => Interlocked.Exchange(ref _consecutiveTriggerTicks, 0);

        /// <summary>
        /// Gets the current consecutive trigger ticks value.
        /// </summary>
        public int ConsecutiveTriggerTicks => Volatile.Read(ref _consecutiveTriggerTicks);

        public long? StopOrderId { get; set; }
        public DateTimeOffset? LastStopOrderUpdate { get; set; }
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
        _stopStates.Clear();
    }
}
