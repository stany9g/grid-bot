using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Connectivity;
using GridBot.ApiService.Services.Grid;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Orchestrates all risk monitoring components and coordinates responses.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class RiskSentinel : IRiskSentinel
{
    private readonly ILogger<RiskSentinel> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ILossMonitor _lossMonitor;
    private readonly IFlashCrashDetector _flashCrashDetector;
    private readonly IFlashPumpDetector _flashPumpDetector;
    private readonly ILiquidityMonitor _liquidityMonitor;
    private readonly INonceHealthMonitor _nonceHealthMonitor;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ITradingStateService _tradingState;
    private readonly IGridLifecycleService _gridLifecycle;

    private readonly ConcurrentDictionary<int, MarketRiskState> _marketStates = new();

    /// <summary>
    /// Creates a new RiskSentinel instance.
    /// </summary>
    public RiskSentinel(
        ILogger<RiskSentinel> logger,
        IRiskConfiguration riskConfig,
        ILossMonitor lossMonitor,
        IFlashCrashDetector flashCrashDetector,
        IFlashPumpDetector flashPumpDetector,
        ILiquidityMonitor liquidityMonitor,
        INonceHealthMonitor nonceHealthMonitor,
        IRiskEventLogger eventLogger,
        ITradingStateService tradingState,
        IGridLifecycleService gridLifecycle)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(riskConfig);
        ArgumentNullException.ThrowIfNull(lossMonitor);
        ArgumentNullException.ThrowIfNull(flashCrashDetector);
        ArgumentNullException.ThrowIfNull(flashPumpDetector);
        ArgumentNullException.ThrowIfNull(liquidityMonitor);
        ArgumentNullException.ThrowIfNull(nonceHealthMonitor);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(tradingState);
        ArgumentNullException.ThrowIfNull(gridLifecycle);

        _logger = logger;
        _riskConfig = riskConfig;
        _lossMonitor = lossMonitor;
        _flashCrashDetector = flashCrashDetector;
        _flashPumpDetector = flashPumpDetector;
        _liquidityMonitor = liquidityMonitor;
        _nonceHealthMonitor = nonceHealthMonitor;
        _eventLogger = eventLogger;
        _tradingState = tradingState;
        _gridLifecycle = gridLifecycle;
    }

    /// <inheritdoc />
    public async Task<RiskAssessment> AssessRiskAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        // Run all risk checks concurrently
        var lossTask = _lossMonitor.GetCurrentLossStatusAsync(marketId, ct);
        var crashTask = _flashCrashDetector.CheckForFlashCrashAsync(marketId, ct);
        var pumpTask = _flashPumpDetector.CheckForFlashPumpAsync(marketId, ct);
        var liquidityTask = _liquidityMonitor.CheckLiquidityAsync(marketId, ct);
        var recentEventsTask = _eventLogger.GetRecentEventsAsync(10, ct);

        await Task.WhenAll(lossTask, crashTask, pumpTask, liquidityTask, recentEventsTask).ConfigureAwait(false);

        var lossStatus = lossTask.Result;
        var crashStatus = crashTask.Result;
        var pumpStatus = pumpTask.Result;
        var liquidityStatus = liquidityTask.Result;
        var recentEvents = recentEventsTask.Result;

        // Determine trading permission
        var tradingAllowed = true;
        var buysBlocked = false;
        var sellsBlocked = false;
        var warnings = new List<string>();
        var overallSeverity = AlertSeverity.Low;

        // Loss limit checks (using rolling windows)
        if (lossStatus.AnyLimitBreached)
        {
            tradingAllowed = false;
            overallSeverity = AlertSeverity.Critical;
            if (lossStatus.Rolling24hBreached) warnings.Add("Rolling 24h loss limit breached");
            if (lossStatus.Rolling7dBreached) warnings.Add("Rolling 7d loss limit breached");
            if (lossStatus.Rolling30dBreached) warnings.Add("Rolling 30d loss limit breached");
            if (lossStatus.DrawdownBreached) warnings.Add("Max drawdown breached");
        }

        // Flash crash checks
        if (crashStatus.CrashDetected || crashStatus.IsInProtection)
        {
            switch (crashStatus.RequiredAction)
            {
                case FlashCrashAction.PauseBuys:
                    buysBlocked = true;
                    if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
                    warnings.Add("Flash crash: BUYs paused");
                    break;

                case FlashCrashAction.PauseAll:
                    tradingAllowed = false;
                    if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
                    warnings.Add("Flash crash: All orders paused");
                    break;

                case FlashCrashAction.CancelAndReduceHalf:
                case FlashCrashAction.FullHalt:
                    tradingAllowed = false;
                    overallSeverity = AlertSeverity.Critical;
                    warnings.Add($"Flash crash: {crashStatus.RequiredAction}");
                    break;

                case FlashCrashAction.EmergencyReduceAndHalt:
                    tradingAllowed = false;
                    overallSeverity = AlertSeverity.Critical;
                    warnings.Add($"BLACK SWAN: {crashStatus.RequiredAction}");
                    if (crashStatus.RequiresManualRestart)
                    {
                        warnings.Add("MANUAL RESTART REQUIRED");
                    }
                    break;
            }
        }

        // Flash pump checks (symmetric protection for SHORT positions)
        if (pumpStatus.PumpDetected || pumpStatus.IsInProtection)
        {
            switch (pumpStatus.RequiredAction)
            {
                case FlashPumpAction.PauseSells:
                    sellsBlocked = true;
                    if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
                    warnings.Add("Flash pump: SELLs paused");
                    break;

                case FlashPumpAction.PauseAll:
                    tradingAllowed = false;
                    if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
                    warnings.Add("Flash pump: All orders paused");
                    break;

                case FlashPumpAction.CancelAndCoverHalf:
                case FlashPumpAction.FullHalt:
                    tradingAllowed = false;
                    overallSeverity = AlertSeverity.Critical;
                    warnings.Add($"Flash pump: {pumpStatus.RequiredAction}");
                    break;
            }
        }

        // Liquidity checks
        if (!liquidityStatus.TradingAllowed)
        {
            tradingAllowed = false;
            if (overallSeverity > AlertSeverity.Critical) overallSeverity = AlertSeverity.Critical;
            warnings.Add("Liquidity conditions not met");
        }
        else if (liquidityStatus.Level == LiquidityLevel.Critical)
        {
            if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
            warnings.Add("Critical liquidity warning");
        }
        else if (liquidityStatus.Level == LiquidityLevel.Low)
        {
            if (overallSeverity > AlertSeverity.Medium) overallSeverity = AlertSeverity.Medium;
            warnings.Add("Low liquidity warning");
        }

        // Add warning text
        if (!string.IsNullOrEmpty(liquidityStatus.Warning))
        {
            warnings.Add(liquidityStatus.Warning);
        }

        // Nonce health checks (H.6 HIGH)
        var nonceStatus = _nonceHealthMonitor.GetStatus();
        if (nonceStatus.ShouldPauseTrading)
        {
            tradingAllowed = false;
            overallSeverity = AlertSeverity.Critical;
            warnings.Add($"Nonce failures: {nonceStatus.ConsecutiveFailures} consecutive - trading paused");
        }
        else if (!nonceStatus.IsHealthy)
        {
            if (overallSeverity > AlertSeverity.High) overallSeverity = AlertSeverity.High;
            warnings.Add($"Nonce warning: {nonceStatus.ConsecutiveFailures} consecutive failures");
        }

        // Calculate position size multiplier
        var positionMultiplier = CalculatePositionMultiplier(lossStatus, crashStatus, pumpStatus, liquidityStatus, nonceStatus);

        // Update state
        state.LastAssessment = DateTimeOffset.UtcNow;
        state.LastPositionMultiplier = positionMultiplier;
        state.LastSpreadMultiplier = liquidityStatus.RecommendedSpreadMultiplier;
        state.TradingAllowed = tradingAllowed;

        var assessment = new RiskAssessment
        {
            MarketId = marketId,
            TradingAllowed = tradingAllowed,
            BuysBlocked = buysBlocked,
            SellsBlocked = sellsBlocked,
            LossStatus = lossStatus,
            FlashCrashStatus = crashStatus,
            FlashPumpStatus = pumpStatus,
            LiquidityStatus = liquidityStatus,
            NonceStatus = nonceStatus,
            OverallSeverity = overallSeverity,
            ActiveWarnings = warnings,
            RecentEvents = recentEvents.ToList(),
            RecommendedPositionMultiplier = positionMultiplier,
            RecommendedSpreadMultiplier = liquidityStatus.RecommendedSpreadMultiplier,
            Timestamp = DateTimeOffset.UtcNow
        };

        if (overallSeverity <= AlertSeverity.High)
        {
            _logger.LogWarning(
                "Risk assessment for market {MarketId}: Severity={Severity}, TradingAllowed={TradingAllowed}, Warnings={Warnings}",
                marketId, overallSeverity, tradingAllowed, string.Join(", ", warnings));
        }

        return assessment;
    }

    /// <inheritdoc />
    public async Task<bool> IsTradingAllowedAsync(int marketId, CancellationToken ct = default)
    {
        // Quick check from cached state
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            // If assessment is recent (< 5 seconds), use cached value
            if (DateTimeOffset.UtcNow - state.LastAssessment < TimeSpan.FromSeconds(5))
            {
                return state.TradingAllowed;
            }
        }

        // Check loss limits (fast)
        if (!await _lossMonitor.CheckLossLimitsAsync(marketId, ct).ConfigureAwait(false))
        {
            return false;
        }

        // Check flash crash protection (fast)
        if (_flashCrashDetector.IsInCrashProtection(marketId))
        {
            var action = _flashCrashDetector.GetCurrentAction(marketId);
            if (action is FlashCrashAction.PauseAll or FlashCrashAction.CancelAndReduceHalf or FlashCrashAction.FullHalt or FlashCrashAction.EmergencyReduceAndHalt)
            {
                return false;
            }
        }

        // Check if manual restart is required (black swan protection)
        if (_flashCrashDetector.RequiresManualRestart(marketId))
        {
            return false;
        }

        // Check flash pump protection (fast)
        if (_flashPumpDetector.IsInPumpProtection(marketId))
        {
            var action = _flashPumpDetector.GetCurrentAction(marketId);
            if (action is FlashPumpAction.PauseAll or FlashPumpAction.CancelAndCoverHalf or FlashPumpAction.FullHalt)
            {
                return false;
            }
        }

        // Check liquidity (fast)
        if (!_liquidityMonitor.IsTradingAllowed(marketId))
        {
            return false;
        }

        // Check nonce health (fast)
        if (_nonceHealthMonitor.ShouldPauseTrading)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task HandleRiskEventAsync(RiskEvent riskEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(riskEvent);

        // Log the event
        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

        // Take action based on severity - NEVER HALT, use Degraded states instead
        if (riskEvent.Severity == AlertSeverity.Critical)
        {
            _logger.LogCritical(
                "CRITICAL RISK EVENT [{RuleId}]: {Description}. Action: {Action}",
                riskEvent.RuleId, riskEvent.Description, riskEvent.ActionTaken);

            // Transition to protective mode - bot continues at minimum capacity
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                $"Critical risk event: {riskEvent.RuleId}").ConfigureAwait(false);

            // Pause grid operations (but keep monitoring)
            await _gridLifecycle.PauseGridAsync(_riskConfig.MarketId, ct).ConfigureAwait(false);
        }
        else if (riskEvent.Severity == AlertSeverity.High)
        {
            _logger.LogError(
                "HIGH SEVERITY RISK EVENT [{RuleId}]: {Description}. Action: {Action}",
                riskEvent.RuleId, riskEvent.Description, riskEvent.ActionTaken);

            // Transition to high volatility mode - reduced capacity
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_HighVolatility,
                $"High severity risk event: {riskEvent.RuleId}").ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RecordPriceUpdateAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        // Record price to both detectors in parallel
        await Task.WhenAll(
            _flashCrashDetector.RecordPriceAsync(marketId, price, ct),
            _flashPumpDetector.RecordPriceAsync(marketId, price, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordEquityUpdateAsync(int marketId, decimal equity, CancellationToken ct = default)
    {
        await _lossMonitor.RecordEquityAsync(marketId, equity, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default)
    {
        await _lossMonitor.RecordTradeResultAsync(marketId, pnlPercent, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public decimal GetPositionSizeMultiplier(int marketId)
    {
        return _marketStates.TryGetValue(marketId, out var state) ? state.LastPositionMultiplier : 1.0m;
    }

    /// <inheritdoc />
    public decimal GetSpreadMultiplier(int marketId)
    {
        return _marketStates.TryGetValue(marketId, out var state) ? state.LastSpreadMultiplier : 1.0m;
    }

    private MarketRiskState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketRiskState());
    }

    private decimal CalculatePositionMultiplier(
        RollingLossStatus lossStatus,
        FlashCrashStatus crashStatus,
        FlashPumpStatus pumpStatus,
        LiquidityStatus liquidityStatus,
        NonceHealthStatus nonceStatus)
    {
        var multiplier = 1.0m;

        // Max drawdown reduction (75% reduction = 0.25 multiplier)
        if (lossStatus.DrawdownBreached)
        {
            multiplier = Math.Min(multiplier, 1.0m - (_riskConfig.LossLimits.DrawdownPositionReductionPercent / 100m));
        }

        // Flash crash reductions
        if (crashStatus.RequiredAction == FlashCrashAction.CancelAndReduceHalf)
        {
            multiplier = Math.Min(multiplier, 0.5m);
        }
        else if (crashStatus.RequiredAction == FlashCrashAction.FullHalt)
        {
            multiplier = Math.Min(multiplier, 0.5m);
        }
        else if (crashStatus.RequiredAction == FlashCrashAction.EmergencyReduceAndHalt)
        {
            // Black swan - use configured target (default 50%)
            multiplier = Math.Min(multiplier, _riskConfig.FlashCrash.BlackSwanPositionTargetPercent);
        }

        // Flash pump reductions (symmetric with flash crash)
        if (pumpStatus.RequiredAction == FlashPumpAction.CancelAndCoverHalf)
        {
            multiplier = Math.Min(multiplier, 0.5m);
        }
        else if (pumpStatus.RequiredAction == FlashPumpAction.FullHalt)
        {
            multiplier = Math.Min(multiplier, 0.5m);
        }

        // Funding rate reduction
        var fundingReduction = _liquidityMonitor.GetFundingRateReduction(liquidityStatus.FundingRate > 0 ? _riskConfig.MarketId : 0);
        if (fundingReduction > 0)
        {
            multiplier = Math.Min(multiplier, 1.0m - (fundingReduction / 100m));
        }

        // If trading not allowed, set to 0
        if (!liquidityStatus.TradingAllowed || lossStatus.AnyLimitBreached || nonceStatus.ShouldPauseTrading)
        {
            multiplier = 0m;
        }

        return Math.Max(0m, multiplier);
    }

    /// <summary>
    /// Internal state tracking for a single market.
    /// </summary>
    private sealed class MarketRiskState
    {
        public DateTimeOffset LastAssessment { get; set; } = DateTimeOffset.MinValue;
        public decimal LastPositionMultiplier { get; set; } = 1.0m;
        public decimal LastSpreadMultiplier { get; set; } = 1.0m;
        public bool TradingAllowed { get; set; } = true;
    }
}
