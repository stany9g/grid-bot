using System.Collections.Concurrent;
using GridBot.MoonBag.Models;
using Microsoft.Extensions.Logging;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Manages moon bag position protection and state machine transitions.
/// Thread-safe implementation with per-market state management.
/// </summary>
public sealed class MoonBagManager : IMoonBagManager, IDisposable
{
    private readonly IMoonBagMarketDataProvider _marketDataProvider;
    private readonly IMoonBagTrendProvider _trendProvider;
    private readonly IFlashSpikeDetector _flashSpikeDetector;
    private readonly IMoonBagConfiguration _config;
    private readonly IMoonBagEventLogger _eventLogger;
    private readonly IMoonBagStateRepository _stateRepository;
    private readonly ILogger<MoonBagManager> _logger;

    private readonly ConcurrentDictionary<int, MoonBagStatus> _moonBagStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    private const int MovingAveragePeriod = 200;

    public MoonBagManager(
        IMoonBagMarketDataProvider marketDataProvider,
        IMoonBagTrendProvider trendProvider,
        IFlashSpikeDetector flashSpikeDetector,
        IMoonBagConfiguration config,
        IMoonBagEventLogger eventLogger,
        IMoonBagStateRepository stateRepository,
        ILogger<MoonBagManager> logger)
    {
        ArgumentNullException.ThrowIfNull(marketDataProvider);
        ArgumentNullException.ThrowIfNull(trendProvider);
        ArgumentNullException.ThrowIfNull(flashSpikeDetector);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(stateRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _marketDataProvider = marketDataProvider;
        _trendProvider = trendProvider;
        _flashSpikeDetector = flashSpikeDetector;
        _config = config;
        _eventLogger = eventLogger;
        _stateRepository = stateRepository;
        _logger = logger;
    }

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
            var now = DateTimeOffset.UtcNow;
            var positionValue = positionSize * entryPrice;

            if (positionValue < _config.MinimumMoonBagUsd)
            {
                _logger.LogInformation(
                    "Position value {Value:C} below minimum {Minimum:C} for moon bag protection on market {MarketId}",
                    positionValue, _config.MinimumMoonBagUsd, marketId);

                var inactiveStatus = MoonBagStatus.Inactive(marketId);
                _moonBagStates[marketId] = inactiveStatus;
                return inactiveStatus;
            }

            if (!isLong && !_config.EnableShortMoonBag)
            {
                _logger.LogInformation(
                    "Short moon bag protection disabled for market {MarketId}",
                    marketId);

                var inactiveStatus = MoonBagStatus.Inactive(marketId);
                _moonBagStates[marketId] = inactiveStatus;
                return inactiveStatus;
            }

            var moonBagQuantity = positionSize * _config.MoonBagPercentage;

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
            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

            var evt = new MoonBagEvent(
                "MB-ACT-001",
                MoonBagAlertSeverity.Medium,
                $"Moon bag protection initialized for market {marketId}",
                $"Position: {positionSize:F4}, Entry: {entryPrice:F4}, Moon bag: {moonBagQuantity:F4}",
                positionSize,
                _config.MinimumMoonBagUsd);

            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Moon bag initialized for market {MarketId}: position={Position:F4}, moonBag={MoonBag:F4}, warmUp until {WarmUpEnd}",
                marketId, positionSize, moonBagQuantity, now.AddMinutes(_config.WarmUpPeriodMinutes));

            return status;
        }
        finally
        {
            marketLock.Release();
        }
    }

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

            if (status.State is not (MoonBagState.Tracking or MoonBagState.Trailing))
            {
                return false;
            }

            var isFlashSpikeActive = await _flashSpikeDetector.IsFlashSpikeActiveAsync(marketId, ct).ConfigureAwait(false);
            if (isFlashSpikeActive)
            {
                _logger.LogDebug(
                    "High watermark update suspended during flash spike for market {MarketId}. Price: {Price:F4}, Current watermark: {Watermark:F4}",
                    marketId, price, status.HighWatermarkPrice);
                return false;
            }

            var threshold = status.HighWatermarkPrice * (1 + _config.HighWatermarkUpdateThreshold);

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

    private decimal CalculateMoonBagThresholdInternal(int marketId)
    {
        if (!_moonBagStates.TryGetValue(marketId, out var status))
        {
            return 0m;
        }

        return status.MaxPositionAchieved * _config.MoonBagPercentage;
    }

    public async Task<bool> IsPositionAtMoonBagLevelAsync(
        int marketId,
        decimal currentPositionSize,
        CancellationToken ct = default)
    {
        var threshold = await CalculateMoonBagThresholdAsync(marketId, ct).ConfigureAwait(false);
        return currentPositionSize <= threshold;
    }

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

            if (status.State is MoonBagState.Inactive or MoonBagState.Released)
            {
                return false;
            }

            var threshold = CalculateMoonBagThresholdInternal(marketId);
            var remainingAfterSell = currentPositionSize - sellQuantity;

            if (remainingAfterSell < threshold)
            {
                _logger.LogWarning(
                    "Sell order blocked for market {MarketId}: {Sell:F4} would breach moon bag threshold {Threshold:F4}",
                    marketId, sellQuantity, threshold);

                var evt = new MoonBagEvent(
                    "TG-002",
                    MoonBagAlertSeverity.High,
                    $"Sell order blocked by moon bag protection for market {marketId}",
                    $"Sell: {sellQuantity:F4}, Position: {currentPositionSize:F4}, Threshold: {threshold:F4}",
                    sellQuantity,
                    threshold);

                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);

                return true;
            }

            return false;
        }
        finally
        {
            marketLock.Release();
        }
    }

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

            var severity = newState switch
            {
                MoonBagState.Triggered => MoonBagAlertSeverity.High,
                MoonBagState.HoldMode => MoonBagAlertSeverity.High,
                MoonBagState.Released => MoonBagAlertSeverity.Medium,
                _ => MoonBagAlertSeverity.Low
            };

            var evt = new MoonBagEvent(
                "MB-STATE",
                severity,
                $"Moon bag state transition for market {marketId}: {oldState} -> {newState}",
                reason,
                0,
                0);

            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
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

    private async Task<bool> CheckReleaseConditionsInternalAsync(
        MoonBagStatus status,
        int marketId,
        CancellationToken ct)
    {
        if (status.State != MoonBagState.HoldMode)
        {
            return false;
        }

        if (_trendProvider.CurrentTrendState != MoonBagTrendState.StrongBear)
        {
            _logger.LogDebug(
                "Release conditions not met for market {MarketId}: trend is {Trend}, not StrongBear",
                marketId, _trendProvider.CurrentTrendState);
            return false;
        }

        try
        {
            var closePrices = await _marketDataProvider.GetClosingPricesAsync(marketId, "1d", MovingAveragePeriod + 10, ct)
                .ConfigureAwait(false);

            if (closePrices.Count < MovingAveragePeriod)
            {
                _logger.LogWarning(
                    "Insufficient candle data for 200 MA check on market {MarketId}",
                    marketId);
                return false;
            }

            var ma200 = _marketDataProvider.CalculateSma(closePrices, MovingAveragePeriod);
            var currentPrice = await _marketDataProvider.GetCurrentPriceAsync(marketId, ct).ConfigureAwait(false);

            if (currentPrice >= ma200)
            {
                _logger.LogDebug(
                    "Release conditions not met for market {MarketId}: price {Price:F4} >= MA200 {Ma:F4}",
                    marketId, currentPrice, ma200);
                return false;
            }

            var ma50 = _marketDataProvider.CalculateSma(closePrices, 50);
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

            if (status.State != MoonBagState.HoldMode)
            {
                _logger.LogWarning(
                    "Cannot approve release for market {MarketId}: not in HOLD_MODE (current: {State})",
                    marketId, status.State);
                return status;
            }

            var conditionsMet = await CheckReleaseConditionsInternalAsync(status, marketId, ct).ConfigureAwait(false);
            if (!conditionsMet)
            {
                _logger.LogWarning(
                    "Cannot approve release for market {MarketId}: release conditions not met",
                    marketId);
                return status;
            }

            status.IsReleaseApproved = true;

            var evt = new MoonBagEvent(
                "TG-005",
                MoonBagAlertSeverity.High,
                $"Moon bag release approved for market {marketId}",
                $"Locked quantity: {status.LockedQuantity:F4}",
                status.LockedQuantity,
                0);

            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Moon bag release approved for market {MarketId}. Locked quantity: {Quantity:F4}",
                marketId, status.LockedQuantity);

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

            if (isLong.HasValue && status.State != MoonBagState.Inactive)
            {
                if (status.IsLongPosition != isLong.Value)
                {
                    _logger.LogWarning(
                        "Position direction changed for market {MarketId}: {OldDirection} -> {NewDirection}. Resetting moon bag state.",
                        marketId,
                        status.IsLongPosition ? "Long" : "Short",
                        isLong.Value ? "Long" : "Short");

                    status.State = MoonBagState.Inactive;
                    status.MaxPositionAchieved = 0;
                    status.HighWatermarkPrice = 0;
                    status.LockedQuantity = 0;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = "Position direction changed";

                    if (!isLong.Value && !_config.EnableShortMoonBag)
                    {
                        _logger.LogInformation(
                            "Short moon bag protection disabled for market {MarketId}, remaining inactive",
                            marketId);
                        return false;
                    }

                    var directionEvent = new MoonBagEvent(
                        "MB-DIR-CHANGE",
                        MoonBagAlertSeverity.High,
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
            status.LockedQuantity = currentPositionSize * _config.MoonBagPercentage;

            _logger.LogInformation(
                "Max position updated for market {MarketId}: {Old:F4} -> {New:F4}, new moon bag: {MoonBag:F4}",
                marketId, oldMax, currentPositionSize, status.LockedQuantity);

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
                profitPercent = (status.EntryPrice - currentPrice) / status.EntryPrice;
            }

            status.CurrentProfitPercent = profitPercent;

            if (status.State == MoonBagState.WarmingUp && profitPercent > 0.05m)
            {
                status.State = MoonBagState.Tracking;
                status.LastStateTransition = DateTimeOffset.UtcNow;
                status.StateReason = $"Early activation: profit {profitPercent:P1} > 5%";

                _logger.LogInformation(
                    "Early moon bag activation for market {MarketId}: profit {Profit:P1} > 5%",
                    marketId, profitPercent);
            }

            if (status.State == MoonBagState.WarmingUp && status.WarmUpStartedAt.HasValue)
            {
                var warmUpEnd = status.WarmUpStartedAt.Value.AddMinutes(_config.WarmUpPeriodMinutes);

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

            if (status.State == MoonBagState.Tracking && status.InitialGridUpperBound > 0)
            {
                var activationPrice = status.InitialGridUpperBound * (1 + _config.TrailingStopActivationThreshold);

                if (currentPrice > activationPrice)
                {
                    status.State = MoonBagState.Trailing;
                    status.LastStateTransition = DateTimeOffset.UtcNow;
                    status.StateReason = $"Price {currentPrice:F4} > activation threshold {activationPrice:F4}";

                    _logger.LogInformation(
                        "Moon bag transitioning to TRAILING for market {MarketId}: price {Price:F4} > activation {Activation:F4}",
                        marketId, currentPrice, activationPrice);

                    var trailingEvent = new MoonBagEvent(
                        "MB-TRAIL-ACT",
                        MoonBagAlertSeverity.Medium,
                        $"Trailing stop activated for market {marketId}",
                        $"Price {currentPrice:F4} exceeded {_config.TrailingStopActivationThreshold:P0} threshold above initial grid",
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

    public async Task ResetMoonBagAsync(int marketId, CancellationToken ct = default)
    {
        var marketLock = GetMarketLock(marketId);
        await marketLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var status = MoonBagStatus.Inactive(marketId);
            _moonBagStates[marketId] = status;
            await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

            _logger.LogInformation("Moon bag reset for market {MarketId}", marketId);
        }
        finally
        {
            marketLock.Release();
        }
    }

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

    public async Task<bool> CheckAndPerformAutoReleaseAsync(
        int marketId,
        decimal currentPrice,
        MoonBagTrendState currentTrend,
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

            if (status.State != MoonBagState.HoldMode)
            {
                return false;
            }

            if (!_config.AutoReleaseEnabled)
            {
                status.AutoReleaseBlockedReason = "Auto-release disabled in configuration";
                return false;
            }

            if (status.OperatorDisabledAutoRelease && _config.AllowOperatorOverride)
            {
                status.AutoReleaseBlockedReason = "Operator disabled auto-release for this market";
                return false;
            }

            if (currentTrend == MoonBagTrendState.StrongBear)
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

            if (status.EntryPrice <= 0)
            {
                status.AutoReleaseBlockedReason = "No entry price available";
                return false;
            }

            decimal unrealizedLossPercent;
            if (status.IsLongPosition)
            {
                unrealizedLossPercent = (currentPrice - status.EntryPrice) / status.EntryPrice;
            }
            else
            {
                unrealizedLossPercent = (status.EntryPrice - currentPrice) / status.EntryPrice;
            }

            if (unrealizedLossPercent <= _config.AutoReleaseUnrealizedLossPercent)
            {
                return await PerformAutoReleaseAsync(
                    marketId, status,
                    $"Unrealized loss {unrealizedLossPercent:P1} exceeds threshold {_config.AutoReleaseUnrealizedLossPercent:P1}",
                    unrealizedLossPercent, ct).ConfigureAwait(false);
            }

            var strongBearDuration = DateTimeOffset.UtcNow - status.StrongBearStartTime.Value;
            var requiredDuration = TimeSpan.FromHours(_config.AutoReleaseConfirmationHours);

            if (strongBearDuration < requiredDuration)
            {
                status.AutoReleaseEligible = false;
                status.AutoReleaseBlockedReason = $"StrongBear duration {strongBearDuration.TotalHours:F1}h < required {_config.AutoReleaseConfirmationHours}h";
                return false;
            }

            var releaseConditionsMet = await CheckReleaseConditionsInternalAsync(status, marketId, ct).ConfigureAwait(false);
            if (!releaseConditionsMet)
            {
                status.AutoReleaseEligible = false;
                status.AutoReleaseBlockedReason = "Price not below MA50 and MA200";
                return false;
            }

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

        status.State = MoonBagState.Released;
        status.IsReleaseApproved = true;
        status.LastStateTransition = DateTimeOffset.UtcNow;
        status.StateReason = $"AUTO-RELEASE: {reason}";

        var evt = new MoonBagEvent(
            "MB-AUTO-REL",
            MoonBagAlertSeverity.Critical,
            $"Moon bag AUTO-RELEASED for market {marketId}",
            $"Reason: {reason}. Quantity: {status.LockedQuantity:F4}, Unrealized loss: {unrealizedLossPercent:P1}",
            status.LockedQuantity,
            unrealizedLossPercent);

        await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
        await _stateRepository.SaveMoonBagStatusAsync(marketId, status, ct).ConfigureAwait(false);

        return true;
    }

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

    private static bool IsValidTransition(MoonBagState from, MoonBagState to)
    {
        return (from, to) switch
        {
            (MoonBagState.Inactive, MoonBagState.WarmingUp) => true,
            (MoonBagState.WarmingUp, MoonBagState.Tracking) => true,
            (MoonBagState.WarmingUp, MoonBagState.Inactive) => true,
            (MoonBagState.Tracking, MoonBagState.Trailing) => true,
            (MoonBagState.Tracking, MoonBagState.HoldMode) => true,
            (MoonBagState.Tracking, MoonBagState.Inactive) => true,
            (MoonBagState.Trailing, MoonBagState.Triggered) => true,
            (MoonBagState.Trailing, MoonBagState.HoldMode) => true,
            (MoonBagState.Trailing, MoonBagState.Tracking) => true,
            (MoonBagState.Trailing, MoonBagState.Inactive) => true,
            (MoonBagState.Triggered, MoonBagState.HoldMode) => true,
            (MoonBagState.Triggered, MoonBagState.Inactive) => true,
            (MoonBagState.HoldMode, MoonBagState.Tracking) => true,
            (MoonBagState.HoldMode, MoonBagState.Released) => true,
            (MoonBagState.HoldMode, MoonBagState.Inactive) => true,
            (MoonBagState.Released, MoonBagState.Inactive) => true,
            _ => false
        };
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
        _moonBagStates.Clear();
    }
}
