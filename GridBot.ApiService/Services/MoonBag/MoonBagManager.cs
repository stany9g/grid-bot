// ============================================================================
// DEPRECATED: This service is part of the legacy ApiService implementation.
// It will be replaced by the modular architecture in GridBot.Core, 
// GridBot.TrendIntelligence, GridBot.MoonBag, and GridBot.AdvancedRisk.
// See REFACTORING_PROGRESS.md for migration status.
// ============================================================================
using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Persistence;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Manages moon bag position protection and state machine transitions.
/// Thread-safe implementation with per-market state management.
/// </summary>
public sealed class MoonBagManager : IMoonBagManager, IDisposable
{
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorService _indicatorService;
    private readonly ITradingStateService _tradingStateService;
    private readonly IFlashSpikeDetector _flashSpikeDetector;
    private readonly IRiskConfiguration _config;
    private readonly IRiskEventLogger _eventLogger;
    private readonly IStateRepository _stateRepository;
    private readonly ILogger<MoonBagManager> _logger;

    private readonly ConcurrentDictionary<int, MoonBagStatus> _moonBagStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    /// <summary>
    /// Moving average period for release condition check.
    /// </summary>
    private const int MovingAveragePeriod = 200;

    public MoonBagManager(
        IMarketDataService marketDataService,
        IIndicatorService indicatorService,
        ITradingStateService tradingStateService,
        IFlashSpikeDetector flashSpikeDetector,
        IRiskConfiguration config,
        IRiskEventLogger eventLogger,
        IStateRepository stateRepository,
        ILogger<MoonBagManager> logger)
    {
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(indicatorService);
        ArgumentNullException.ThrowIfNull(tradingStateService);
        ArgumentNullException.ThrowIfNull(flashSpikeDetector);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(stateRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _marketDataService = marketDataService;
        _indicatorService = indicatorService;
        _tradingStateService = tradingStateService;
        _flashSpikeDetector = flashSpikeDetector;
        _config = config;
        _eventLogger = eventLogger;
        _stateRepository = stateRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MoonBagStatus> GetMoonBagStatusAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _moonBagStates.GetOrAdd(marketId, _ => MoonBagStatus.Inactive(marketId));
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MoonBagStatus> InitializeMoonBagAsync(
        int marketId,
        decimal positionSize,
        decimal entryPrice,
        decimal gridUpperBound,
        bool isLong = true,
        CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var options = _config.MoonBag;
            var now = DateTimeOffset.UtcNow;

            // Check if position meets minimum requirements
            var positionValue = positionSize * entryPrice;
            if (positionValue < options.MinimumMoonBagUsd)
            {
                _logger.LogInformation(
                    "Position value {Value:C} below minimum {Minimum:C} for moon bag protection on market {MarketId}",
                    positionValue, options.MinimumMoonBagUsd, marketId);

                var inactiveStatus = MoonBagStatus.Inactive(marketId);
                _moonBagStates[marketId] = inactiveStatus;
                return inactiveStatus;
            }

            // Check if short moon bag is enabled
            if (!isLong && !options.EnableShortMoonBag)
            {
                _logger.LogInformation(
                    "Short moon bag protection disabled for market {MarketId}",
                    marketId);

                var inactiveStatus = MoonBagStatus.Inactive(marketId);
                _moonBagStates[marketId] = inactiveStatus;
                return inactiveStatus;
            }

            // Calculate initial moon bag threshold
            var moonBagQuantity = positionSize * options.MoonBagPercentage;

            var status = new MoonBagStatus
            {
                MarketId = marketId,
                State = MoonBagState.WarmingUp,
                MaxPositionAchieved = positionSize,
                HighWatermarkPrice = entryPrice,
                LockedQuantity = moonBagQuantity,
                WarmUpStartedAt = now,
                PositionOpenedAt = now,
                EntryPrice = entryPrice,
                InitialGridUpperBound = gridUpperBound,
                IsLongPosition = isLong,
                LastStateTransition = now,
                StateReason = "Position opened, warm-up period started"
            };

            _moonBagStates[marketId] = status;

            // Persist to Redis
            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

            // Log event
            var riskEvent = RiskEvent.Create(
                "MB-ACT-001",
                AlertSeverity.Medium,
                $"Moon bag protection initialized for market {marketId}",
                $"Position: {positionSize:F4}, Entry: {entryPrice:F4}, Moon bag: {moonBagQuantity:F4}",
                positionSize,
                options.MinimumMoonBagUsd);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Moon bag initialized for market {MarketId}: position={Position:F4}, moonBag={MoonBag:F4}, warmUp until {WarmUpEnd}",
                marketId, positionSize, moonBagQuantity, now.AddMinutes(options.WarmUpPeriodMinutes));

            return status;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHighWatermarkAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return false;
            }

            // Only update in TRACKING or TRAILING states
            if (status.State is not (MoonBagState.Tracking or MoonBagState.Trailing))
            {
                return false;
            }

            // HIGH-002 FIX: Check if flash spike is active - suspend high watermark updates during spike
            // Per spec Rule EC-SPIKE-001: IF price_increase_5min > 20% THEN suspend_high_watermark_updates(10_minutes)
            var isFlashSpikeActive = await _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct).ConfigureAwait(false);
            if (isFlashSpikeActive)
            {
                _logger.LogDebug(
                    "High watermark update suspended during flash spike for market {MarketId}. Price: {Price:F4}, Current watermark: {Watermark:F4}",
                    marketId, price, status.HighWatermarkPrice);
                return false;
            }

            // Check if new price is high enough to warrant an update
            var options = _config.MoonBag;
            var threshold = status.HighWatermarkPrice * (1 + options.HighWatermarkUpdateThreshold);

            if (price <= threshold)
            {
                return false;
            }

            var oldWatermark = status.HighWatermarkPrice;
            status.HighWatermarkPrice = price;
            status.LastHighWatermarkUpdate = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "High watermark updated for market {MarketId}: {Old:F4} -> {New:F4}",
                marketId, oldWatermark, price);

            return true;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<decimal> CalculateMoonBagThresholdAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return CalculateMoonBagThresholdInternal(marketId);
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <summary>
    /// Internal method to calculate moon bag threshold without locking.
    /// Caller must hold the market lock.
    /// </summary>
    private decimal CalculateMoonBagThresholdInternal(int marketId)
    {
        if (!_moonBagStates.TryGetValue(marketId, out var status))
        {
            return 0m;
        }

        var options = _config.MoonBag;
        return status.MaxPositionAchieved * options.MoonBagPercentage;
    }

    /// <inheritdoc />
    public async Task<bool> IsPositionAtMoonBagLevelAsync(
        int marketId,
        decimal currentPositionSize,
        CancellationToken ct = default)
    {
        var threshold = await CalculateMoonBagThresholdAsync(marketId, ct).ConfigureAwait(false);
        return currentPositionSize <= threshold;
    }

    /// <inheritdoc />
    public async Task<bool> ShouldBlockSellOrderAsync(
        int marketId,
        decimal sellQuantity,
        decimal currentPositionSize,
        CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return false;
            }

            // Only block in moon bag protection states
            if (status.State is MoonBagState.Inactive or MoonBagState.Released)
            {
                return false;
            }

            var threshold = CalculateMoonBagThresholdInternal(marketId);
            var remainingAfterSell = currentPositionSize - sellQuantity;

            // Block if sell would reduce position below moon bag threshold
            if (remainingAfterSell < threshold)
            {
                _logger.LogWarning(
                    "Sell order blocked for market {MarketId}: {Sell:F4} would breach moon bag threshold {Threshold:F4}",
                    marketId, sellQuantity, threshold);

                // Log event
                var riskEvent = RiskEvent.Create(
                    "TG-002",
                    AlertSeverity.High,
                    $"Sell order blocked by moon bag protection for market {marketId}",
                    $"Sell: {sellQuantity:F4}, Position: {currentPositionSize:F4}, Threshold: {threshold:F4}",
                    sellQuantity,
                    threshold);

                await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

                return true;
            }

            return false;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MoonBagStatus> TransitionStateAsync(
        int marketId,
        MoonBagState newState,
        string reason,
        CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                status = MoonBagStatus.Inactive(marketId);
                _moonBagStates[marketId] = status;
            }

            var oldState = status.State;

            // Validate transition
            if (!IsValidTransition(oldState, newState))
            {
                _logger.LogWarning(
                    "Invalid moon bag state transition for market {MarketId}: {Old} -> {New}",
                    marketId, oldState, newState);
                return status;
            }

            status.State = newState;
            status.LastStateTransition = DateTimeOffset.UtcNow;
            status.StateReason = reason;

            // Log event
            var severity = newState switch
            {
                MoonBagState.Triggered => AlertSeverity.High,
                MoonBagState.HoldMode => AlertSeverity.High,
                MoonBagState.Released => AlertSeverity.Medium,
                _ => AlertSeverity.Low
            };

            var riskEvent = RiskEvent.Create(
                "MB-STATE",
                severity,
                $"Moon bag state transition for market {marketId}: {oldState} -> {newState}",
                reason,
                0,
                0);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            // Persist to Redis
            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Moon bag state transition for market {MarketId}: {Old} -> {New}. Reason: {Reason}",
                marketId, oldState, newState, reason);

            return status;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckReleaseConditionsAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return false;
            }

            return await CheckReleaseConditionsInternalAsync(status, marketId, ct).ConfigureAwait(false);
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <summary>
    /// Internal method to check release conditions without locking.
    /// Caller must hold the market lock.
    /// </summary>
    private async Task<bool> CheckReleaseConditionsInternalAsync(
        MoonBagStatus status,
        int marketId,
        CancellationToken ct)
    {
        // Must be in HOLD_MODE to check release conditions
        if (status.State != MoonBagState.HoldMode)
        {
            return false;
        }

        // Check trend state - must be STRONG_BEAR
        if (_tradingStateService.CurrentTrendState != TrendState.StrongBear)
        {
            _logger.LogDebug(
                "Release conditions not met for market {MarketId}: trend is {Trend}, not StrongBear",
                marketId, _tradingStateService.CurrentTrendState);
            return false;
        }

        // Check price vs 200 MA
        try
        {
            var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1d", MovingAveragePeriod + 10, ct)
                .ConfigureAwait(false);

            if (candles.Count < MovingAveragePeriod)
            {
                _logger.LogWarning(
                    "Insufficient candle data for 200 MA check on market {MarketId}",
                    marketId);
                return false;
            }

            var closePrices = candles.Select(c => c.Close).ToList();
            var ma200 = _indicatorService.CalculateSma(closePrices, MovingAveragePeriod);

            var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            if (currentPrice >= ma200)
            {
                _logger.LogDebug(
                    "Release conditions not met for market {MarketId}: price {Price:F4} >= MA200 {Ma:F4}",
                    marketId, currentPrice, ma200);
                return false;
            }

            // Also check 50 MA as additional confirmation
            var ma50 = _indicatorService.CalculateSma(closePrices, 50);
            if (currentPrice >= ma50)
            {
                _logger.LogDebug(
                    "Release conditions not met for market {MarketId}: price {Price:F4} >= MA50 {Ma:F4}",
                    marketId, currentPrice, ma50);
                return false;
            }

            _logger.LogInformation(
                "Release conditions met for market {MarketId}: StrongBear, price {Price:F4} < MA50 {Ma50:F4} < MA200 {Ma200:F4}",
                marketId, currentPrice, ma50, ma200);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking release conditions for market {MarketId}", marketId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<MoonBagStatus> ApproveReleaseAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return MoonBagStatus.Inactive(marketId);
            }

            // Must be in HOLD_MODE
            if (status.State != MoonBagState.HoldMode)
            {
                _logger.LogWarning(
                    "Cannot approve release for market {MarketId}: not in HOLD_MODE (current: {State})",
                    marketId, status.State);
                return status;
            }

            // Check release conditions - use internal method to avoid deadlock
            var conditionsMet = await CheckReleaseConditionsInternalAsync(status, marketId, ct).ConfigureAwait(false);
            if (!conditionsMet)
            {
                _logger.LogWarning(
                    "Cannot approve release for market {MarketId}: release conditions not met",
                    marketId);
                return status;
            }

            status.IsReleaseApproved = true;

            // Log event
            var riskEvent = RiskEvent.Create(
                "TG-005",
                AlertSeverity.High,
                $"Moon bag release approved for market {marketId}",
                $"Locked quantity: {status.LockedQuantity:F4}",
                status.LockedQuantity,
                0);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Moon bag release approved for market {MarketId}. Locked quantity: {Quantity:F4}",
                marketId, status.LockedQuantity);

            // Transition to Released state
            status.State = MoonBagState.Released;
            status.LastStateTransition = DateTimeOffset.UtcNow;
            status.StateReason = "Operator approved release after conditions met";

            return status;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMaxPositionAsync(int marketId, decimal currentPositionSize, bool? isLong = null, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return false;
            }

            var options = _config.MoonBag;

            // CRITICAL-004 FIX: Check for position direction change
            if (isLong.HasValue && status.State != MoonBagState.Inactive)
            {
                if (status.IsLongPosition != isLong.Value)
                {
                    _logger.LogWarning(
                        "Position direction changed for market {MarketId}: {OldDirection} -> {NewDirection}. Resetting moon bag state.",
                        marketId,
                        status.IsLongPosition ? "Long" : "Short",
                        isLong.Value ? "Long" : "Short");

                    // Reset the moon bag state for direction change
                    status.State = MoonBagState.Inactive;
                    status.MaxPositionAchieved = 0;
                    status.HighWatermarkPrice = 0;
                    status.LockedQuantity = 0;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = "Position direction changed";

                    // If short moon bag is disabled and new position is short, keep inactive
                    if (!isLong.Value && !options.EnableShortMoonBag)
                    {
                        _logger.LogInformation(
                            "Short moon bag protection disabled for market {MarketId}, remaining inactive",
                            marketId);
                        return false;
                    }

                    // Log event for direction change
                    var directionEvent = RiskEvent.Create(
                        "MB-DIR-CHANGE",
                        AlertSeverity.High,
                        $"Moon bag direction changed for market {marketId}",
                        $"Switched from {(status.IsLongPosition ? "Long" : "Short")} to {(isLong.Value ? "Long" : "Short")}",
                        0,
                        0);

                    await _eventLogger.LogEventAsync(directionEvent, ct).ConfigureAwait(false);
                    return false;
                }
            }

            if (currentPositionSize <= status.MaxPositionAchieved)
            {
                return false;
            }

            var oldMax = status.MaxPositionAchieved;
            status.MaxPositionAchieved = currentPositionSize;

            // Recalculate moon bag threshold
            status.LockedQuantity = currentPositionSize * options.MoonBagPercentage;

            _logger.LogInformation(
                "Max position updated for market {MarketId}: {Old:F4} -> {New:F4}, new moon bag: {MoonBag:F4}",
                marketId, oldMax, currentPositionSize, status.LockedQuantity);

            // If in HOLD_MODE and position increased above threshold, exit HOLD_MODE
            if (status.State == MoonBagState.HoldMode)
            {
                var threshold = status.LockedQuantity;
                if (currentPositionSize > threshold)
                {
                    status.State = MoonBagState.Tracking;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = "Position increased above moon bag threshold";

                    _logger.LogInformation(
                        "Exiting HOLD_MODE for market {MarketId}: position {Position:F4} > threshold {Threshold:F4}",
                        marketId, currentPositionSize, threshold);
                }
            }

            return true;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<decimal> UpdateProfitPercentAsync(int marketId, decimal currentPrice, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return 0m;
            }

            if (status.EntryPrice <= 0)
            {
                return 0m;
            }

            decimal profitPercent;
            if (status.IsLongPosition)
            {
                profitPercent = (currentPrice - status.EntryPrice) / status.EntryPrice;
            }
            else
            {
                // For short positions, profit when price decreases
                profitPercent = (status.EntryPrice - currentPrice) / status.EntryPrice;
            }

            status.CurrentProfitPercent = profitPercent;

            // Check for warm-up early activation (>5% profit)
            if (status.State == MoonBagState.WarmingUp && profitPercent > 0.05m)
            {
                status.State = MoonBagState.Tracking;
                status.LastStateTransition = DateTimeOffset.UtcNow;
                status.StateReason = $"Early activation: profit {profitPercent:P1} > 5%";

                _logger.LogInformation(
                    "Early moon bag activation for market {MarketId}: profit {Profit:P1} > 5%",
                    marketId, profitPercent);
            }

            // Check for warm-up completion
            if (status.State == MoonBagState.WarmingUp && status.WarmUpStartedAt.HasValue)
            {
                var options = _config.MoonBag;
                var warmUpEnd = status.WarmUpStartedAt.Value.AddMinutes(options.WarmUpPeriodMinutes);

                if (DateTimeOffset.UtcNow >= warmUpEnd)
                {
                    status.State = MoonBagState.Tracking;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = "Warm-up period completed";

                    _logger.LogInformation(
                        "Warm-up completed for market {MarketId}, transitioning to TRACKING",
                        marketId);
                }
            }

            // HIGH-004 FIX: Check for TRACKING -> TRAILING state transition
            // Per spec: TRACKING -> TRAILING when price > initial_grid * 1.10
            if (status.State == MoonBagState.Tracking && status.InitialGridUpperBound > 0)
            {
                var options = _config.MoonBag;
                var activationPrice = status.InitialGridUpperBound * (1 + options.TrailingStopActivationThreshold);

                if (currentPrice > activationPrice)
                {
                    status.State = MoonBagState.Trailing;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = $"Price {currentPrice:F4} > activation threshold {activationPrice:F4} ({options.TrailingStopActivationThreshold:P0} above grid)";

                    _logger.LogInformation(
                        "Moon bag transitioning to TRAILING for market {MarketId}: price {Price:F4} > activation {Activation:F4}",
                        marketId, currentPrice, activationPrice);

                    // Log event for trailing activation
                    var trailingEvent = RiskEvent.Create(
                        "MB-TRAIL-ACT",
                        AlertSeverity.Medium,
                        $"Trailing stop activated for market {marketId}",
                        $"Price {currentPrice:F4} exceeded {options.TrailingStopActivationThreshold:P0} threshold above initial grid",
                        currentPrice,
                        activationPrice);

                    await _eventLogger.LogEventAsync(trailingEvent, ct).ConfigureAwait(false);
                }
            }

            return profitPercent;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetMoonBagAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var status = MoonBagStatus.Inactive(marketId);
            _moonBagStates[marketId] = status;

            // Persist to Redis
            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

            _logger.LogInformation("Moon bag reset for market {MarketId}", marketId);
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var status = await _stateRepository.LoadMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

            if (status is null)
            {
                _logger.LogDebug("No persisted moon bag state found for market {MarketId}", marketId);
                return false;
            }

            _moonBagStates[marketId] = status;

            _logger.LogInformation(
                "Loaded persisted moon bag state for market {MarketId}: State={State}, HighWatermark={HighWatermark:F4}",
                marketId, status.State, status.HighWatermarkPrice);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted moon bag state for market {MarketId}", marketId);
            return false;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckAndPerformAutoReleaseAsync(
        int marketId,
        decimal currentPrice,
        TrendState currentTrend,
        CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return false;
            }

            // Only check in HOLD_MODE
            if (status.State != MoonBagState.HoldMode)
            {
                return false;
            }

            var options = _config.MoonBag;

            // Check if auto-release is enabled
            if (!options.AutoReleaseEnabled)
            {
                status.AutoReleaseBlockedReason = "Auto-release disabled in configuration";
                return false;
            }

            // Check operator override
            if (status.OperatorDisabledAutoRelease && options.AllowOperatorOverride)
            {
                status.AutoReleaseBlockedReason = "Operator disabled auto-release for this market";
                return false;
            }

            // Track StrongBear duration
            if (currentTrend == TrendState.StrongBear)
            {
                if (!status.StrongBearStartTime.HasValue)
                {
                    status.StrongBearStartTime = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "StrongBear trend started for market {MarketId}, beginning auto-release countdown",
                        marketId);
                }
            }
            else
            {
                // Reset if trend changes
                if (status.StrongBearStartTime.HasValue)
                {
                    _logger.LogInformation(
                        "Trend changed from StrongBear for market {MarketId}, resetting auto-release countdown",
                        marketId);
                }
                status.StrongBearStartTime = null;
                status.AutoReleaseEligible = false;
                status.AutoReleaseBlockedReason = $"Trend is {currentTrend}, not StrongBear";
                return false;
            }

            // Calculate unrealized loss on moon bag
            decimal unrealizedLossPercent;
            if (status.EntryPrice <= 0)
            {
                status.AutoReleaseBlockedReason = "No entry price available";
                return false;
            }

            if (status.IsLongPosition)
            {
                unrealizedLossPercent = (currentPrice - status.EntryPrice) / status.EntryPrice;
            }
            else
            {
                // For short positions, profit when price decreases (loss when price increases)
                unrealizedLossPercent = (status.EntryPrice - currentPrice) / status.EntryPrice;
            }

            // Check immediate release due to large loss
            if (unrealizedLossPercent <= options.AutoReleaseUnrealizedLossPercent)
            {
                return await PerformAutoReleaseAsync(
                    marketId, status,
                    $"Unrealized loss {unrealizedLossPercent:P1} exceeds threshold {options.AutoReleaseUnrealizedLossPercent:P1}",
                    unrealizedLossPercent, ct).ConfigureAwait(false);
            }

            // Check StrongBear duration
            var strongBearDuration = DateTimeOffset.UtcNow - status.StrongBearStartTime.Value;
            var requiredDuration = TimeSpan.FromHours(options.AutoReleaseConfirmationHours);

            if (strongBearDuration < requiredDuration)
            {
                status.AutoReleaseEligible = false;
                status.AutoReleaseBlockedReason = $"StrongBear duration {strongBearDuration.TotalHours:F1}h < required {options.AutoReleaseConfirmationHours}h";
                return false;
            }

            // Check price vs MAs (must be below both MA50 and MA200)
            var releaseConditionsMet = await CheckReleaseConditionsInternalAsync(status, marketId, ct).ConfigureAwait(false);
            if (!releaseConditionsMet)
            {
                status.AutoReleaseEligible = false;
                status.AutoReleaseBlockedReason = "Price not below MA50 and MA200";
                return false;
            }

            // All conditions met - perform auto-release
            status.AutoReleaseEligible = true;
            return await PerformAutoReleaseAsync(
                marketId, status,
                $"StrongBear for {strongBearDuration.TotalHours:F1}h with price below death cross",
                unrealizedLossPercent, ct).ConfigureAwait(false);
        }
        finally
        {
            marketLock.Release();
        }
    }

    private async Task<bool> PerformAutoReleaseAsync(
        int marketId,
        MoonBagStatus status,
        string reason,
        decimal unrealizedLossPercent,
        CancellationToken ct)
    {
        _logger.LogCritical(
            "AUTO-RELEASING MOON BAG for market {MarketId}: {Reason}. Quantity: {Quantity:F4}, Loss: {Loss:P1}",
            marketId, reason, status.LockedQuantity, unrealizedLossPercent);

        // Transition to Released state
        status.State = MoonBagState.Released;
        status.IsReleaseApproved = true; // Mark as approved (automatically)
        status.LastStateTransition = DateTimeOffset.UtcNow;
        status.StateReason = $"AUTO-RELEASE: {reason}";

        // Log CRITICAL risk event
        var riskEvent = RiskEvent.Create(
            "MB-AUTO-REL",
            AlertSeverity.Critical,
            $"Moon bag AUTO-RELEASED for market {marketId}",
            $"Reason: {reason}. Quantity: {status.LockedQuantity:F4}, Unrealized loss: {unrealizedLossPercent:P1}",
            status.LockedQuantity,
            unrealizedLossPercent);

        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

        // Persist state
        await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task SetOperatorAutoReleaseOverrideAsync(int marketId, bool disabled, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!_moonBagStates.TryGetValue(marketId, out var status))
            {
                return;
            }

            status.OperatorDisabledAutoRelease = disabled;

            _logger.LogWarning(
                "Operator {Action} auto-release for market {MarketId}",
                disabled ? "DISABLED" : "ENABLED", marketId);

            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <inheritdoc />
    public TimeSpan? GetStrongBearDuration(int marketId)
    {
        if (!_moonBagStates.TryGetValue(marketId, out var status))
        {
            return null;
        }

        if (!status.StrongBearStartTime.HasValue)
        {
            return null;
        }

        return DateTimeOffset.UtcNow - status.StrongBearStartTime.Value;
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Validates state machine transitions.
    /// </summary>
    private static bool IsValidTransition(MoonBagState from, MoonBagState to)
    {
        return (from, to) switch
        {
            // From Inactive
            (MoonBagState.Inactive, MoonBagState.WarmingUp) => true,

            // From WarmingUp
            (MoonBagState.WarmingUp, MoonBagState.Tracking) => true,
            (MoonBagState.WarmingUp, MoonBagState.Inactive) => true, // Position closed during warm-up

            // From Tracking
            (MoonBagState.Tracking, MoonBagState.Trailing) => true,
            (MoonBagState.Tracking, MoonBagState.HoldMode) => true, // Position dropped to moon bag level
            (MoonBagState.Tracking, MoonBagState.Inactive) => true, // Position closed

            // From Trailing
            (MoonBagState.Trailing, MoonBagState.Triggered) => true,
            (MoonBagState.Trailing, MoonBagState.HoldMode) => true, // Position dropped to moon bag level
            (MoonBagState.Trailing, MoonBagState.Tracking) => true, // Stop not triggered, continue tracking
            (MoonBagState.Trailing, MoonBagState.Inactive) => true, // Position closed

            // From Triggered
            (MoonBagState.Triggered, MoonBagState.HoldMode) => true,
            (MoonBagState.Triggered, MoonBagState.Inactive) => true, // Position fully closed

            // From HoldMode
            (MoonBagState.HoldMode, MoonBagState.Tracking) => true, // Position increased
            (MoonBagState.HoldMode, MoonBagState.Released) => true, // Release approved
            (MoonBagState.HoldMode, MoonBagState.Inactive) => true, // Position closed

            // From Released
            (MoonBagState.Released, MoonBagState.Inactive) => true, // Moon bag sold

            _ => false
        };
    }

    /// <summary>
    /// Disposes resources used by the manager.
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
        _moonBagStates.Clear();
    }
}

