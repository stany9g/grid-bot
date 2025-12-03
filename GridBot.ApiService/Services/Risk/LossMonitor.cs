using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Persistence;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors P&amp;L and loss limits for trading protection.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class LossMonitor : ILossMonitor, IDisposable
{
    private readonly ILogger<LossMonitor> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingState;
    private readonly IRiskEventLogger _eventLogger;
    private readonly IStateRepository _stateRepository;

    private readonly ConcurrentDictionary<int, MarketLossState> _marketStates = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a new LossMonitor instance.
    /// </summary>
    public LossMonitor(
        ILogger<LossMonitor> logger,
        IRiskConfiguration riskConfig,
        ITradingStateService tradingState,
        IRiskEventLogger eventLogger,
        IStateRepository stateRepository)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(riskConfig);
        ArgumentNullException.ThrowIfNull(tradingState);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(stateRepository);

        _logger = logger;
        _riskConfig = riskConfig;
        _tradingState = tradingState;
        _eventLogger = eventLogger;
        _stateRepository = stateRepository;
    }

    /// <inheritdoc />
    public Task<LossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = GetOrCreateState(marketId);

        return Task.FromResult(new LossStatus
        {
            DailyPnlPercent = state.DailyPnl,
            WeeklyPnlPercent = state.WeeklyPnl,
            MonthlyPnlPercent = state.MonthlyPnl,
            DrawdownFromAthPercent = state.CurrentDrawdown,
            EquityHighWaterMark = state.EquityHighWaterMark,
            CurrentEquity = state.CurrentEquity,
            DailyLimitBreached = state.DailyPnl <= _riskConfig.LossLimits.DailyLossPercent,
            WeeklyLimitBreached = state.WeeklyPnl <= _riskConfig.LossLimits.WeeklyLossPercent,
            MonthlyLimitBreached = state.MonthlyPnl <= _riskConfig.LossLimits.MonthlyLossPercent,
            DrawdownLimitBreached = state.CurrentDrawdown <= _riskConfig.LossLimits.MaxDrawdownPercent,
            HaltUntil = state.HaltUntil,
            HaltReason = state.HaltReason
        });
    }

    /// <inheritdoc />
    public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);

            state.DailyPnl += pnlPercent;
            state.WeeklyPnl += pnlPercent;
            state.MonthlyPnl += pnlPercent;
            state.LastTradeTime = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "Recorded trade P&L for market {MarketId}: {PnlPercent}%. Daily: {Daily}%, Weekly: {Weekly}%, Monthly: {Monthly}%",
                marketId, pnlPercent, state.DailyPnl, state.WeeklyPnl, state.MonthlyPnl);

            // Persist loss status to Redis
            await SaveLossStatusAsync(marketId, state, ct).ConfigureAwait(false);

            // Check for single trade loss limit
            if (pnlPercent <= _riskConfig.LossLimits.SingleTradeLossPercent)
            {
                var riskEvent = RiskEvent.Create(
                    "LOSS-001",
                    AlertSeverity.High,
                    $"Single trade loss of {pnlPercent:F2}% exceeds limit of {_riskConfig.LossLimits.SingleTradeLossPercent}%",
                    "Cancel all pending orders in market",
                    pnlPercent,
                    _riskConfig.LossLimits.SingleTradeLossPercent);

                await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Single trade loss limit breached for market {MarketId}: {PnlPercent}% <= {Limit}%",
                    marketId, pnlPercent, _riskConfig.LossLimits.SingleTradeLossPercent);
            }

            await CheckAndTriggerLimitsAsync(marketId, state, ct).ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordEquityAsync(int marketId, decimal currentEquity, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);

            state.CurrentEquity = currentEquity;

            // Update high water mark if new ATH
            if (currentEquity > state.EquityHighWaterMark)
            {
                state.EquityHighWaterMark = currentEquity;
                _logger.LogInformation(
                    "New equity high water mark for market {MarketId}: {Equity:F2} USD",
                    marketId, currentEquity);
            }

            // Calculate drawdown
            if (state.EquityHighWaterMark > 0)
            {
                state.CurrentDrawdown = ((currentEquity - state.EquityHighWaterMark) / state.EquityHighWaterMark) * 100m;
            }

            // Persist loss status to Redis
            await SaveLossStatusAsync(marketId, state, ct).ConfigureAwait(false);

            await CheckAndTriggerLimitsAsync(marketId, state, ct).ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckLossLimitsAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        // Check if currently halted
        if (state.HaltUntil.HasValue)
        {
            if (DateTimeOffset.UtcNow < state.HaltUntil.Value)
            {
                return false;
            }

            // Halt period expired - clear it
            state.HaltUntil = null;
            state.HaltReason = null;
            _logger.LogInformation("Halt period expired for market {MarketId}", marketId);
        }

        // Check limits
        var limits = _riskConfig.LossLimits;

        if (state.DailyPnl <= limits.DailyLossPercent)
        {
            return false;
        }

        if (state.WeeklyPnl <= limits.WeeklyLossPercent)
        {
            return false;
        }

        if (state.MonthlyPnl <= limits.MonthlyLossPercent)
        {
            return false;
        }

        if (state.CurrentDrawdown <= limits.MaxDrawdownPercent)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void ResetDailyLimits()
    {
        foreach (var state in _marketStates.Values)
        {
            state.DailyPnl = 0;
        }

        _logger.LogInformation("Daily loss limits reset for all markets");
    }

    /// <inheritdoc />
    public void ResetWeeklyLimits()
    {
        foreach (var state in _marketStates.Values)
        {
            state.WeeklyPnl = 0;
        }

        _logger.LogInformation("Weekly loss limits reset for all markets");
    }

    /// <inheritdoc />
    public void ResetMonthlyLimits()
    {
        foreach (var state in _marketStates.Values)
        {
            state.MonthlyPnl = 0;
        }

        _logger.LogInformation("Monthly loss limits reset for all markets");
    }

    /// <inheritdoc />
    public void ClearHalt(int marketId)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            state.HaltUntil = null;
            state.HaltReason = null;
            _logger.LogWarning("Manually cleared halt for market {MarketId}", marketId);
        }
    }

    private MarketLossState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketLossState());
    }

    private async Task CheckAndTriggerLimitsAsync(int marketId, MarketLossState state, CancellationToken ct)
    {
        var limits = _riskConfig.LossLimits;

        // Daily limit check
        if (state.DailyPnl <= limits.DailyLossPercent && state.HaltReason != "DailyLimit")
        {
            state.HaltUntil = DateTimeOffset.UtcNow.AddHours(24);
            state.HaltReason = "DailyLimit";

            var riskEvent = RiskEvent.Create(
                "LOSS-002",
                AlertSeverity.Critical,
                $"Daily loss limit breached: {state.DailyPnl:F2}% <= {limits.DailyLossPercent}%",
                "Halt ALL trading for 24 hours",
                state.DailyPnl,
                limits.DailyLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Halted, "Daily loss limit breached").ConfigureAwait(false);

            _logger.LogCritical(
                "DAILY LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. Trading halted for 24 hours.",
                marketId, state.DailyPnl, limits.DailyLossPercent);
        }

        // Weekly limit check
        if (state.WeeklyPnl <= limits.WeeklyLossPercent && state.HaltReason != "WeeklyLimit")
        {
            state.HaltUntil = DateTimeOffset.UtcNow.AddDays(7);
            state.HaltReason = "WeeklyLimit";

            var riskEvent = RiskEvent.Create(
                "LOSS-003",
                AlertSeverity.Critical,
                $"Weekly loss limit breached: {state.WeeklyPnl:F2}% <= {limits.WeeklyLossPercent}%",
                "Halt ALL trading for 7 days",
                state.WeeklyPnl,
                limits.WeeklyLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Halted, "Weekly loss limit breached").ConfigureAwait(false);

            _logger.LogCritical(
                "WEEKLY LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. Trading halted for 7 days.",
                marketId, state.WeeklyPnl, limits.WeeklyLossPercent);
        }

        // Monthly limit check - requires manual restart
        if (state.MonthlyPnl <= limits.MonthlyLossPercent && state.HaltReason != "MonthlyLimit")
        {
            state.HaltUntil = null; // No automatic expiry
            state.HaltReason = "MonthlyLimit";

            var riskEvent = RiskEvent.Create(
                "LOSS-004",
                AlertSeverity.Critical,
                $"Monthly loss limit breached: {state.MonthlyPnl:F2}% <= {limits.MonthlyLossPercent}%",
                "Halt ALL trading - requires manual restart",
                state.MonthlyPnl,
                limits.MonthlyLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Halted, "Monthly loss limit breached - manual restart required").ConfigureAwait(false);

            _logger.LogCritical(
                "MONTHLY LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. Trading halted - MANUAL RESTART REQUIRED.",
                marketId, state.MonthlyPnl, limits.MonthlyLossPercent);
        }

        // Max drawdown check - reduce position size by 75%
        if (state.CurrentDrawdown <= limits.MaxDrawdownPercent && state.HaltReason != "MaxDrawdown")
        {
            state.HaltReason = "MaxDrawdown";

            var riskEvent = RiskEvent.Create(
                "LOSS-005",
                AlertSeverity.Critical,
                $"Max drawdown breached: {state.CurrentDrawdown:F2}% <= {limits.MaxDrawdownPercent}%",
                $"Reduce all position sizes by {limits.DrawdownPositionReductionPercent}%, alert operator",
                state.CurrentDrawdown,
                limits.MaxDrawdownPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            _logger.LogCritical(
                "MAX DRAWDOWN BREACHED for market {MarketId}: {Drawdown}% <= {Limit}%. Position sizes reduced by {Reduction}%.",
                marketId, state.CurrentDrawdown, limits.MaxDrawdownPercent, limits.DrawdownPositionReductionPercent);
        }
    }

    /// <summary>
    /// Internal state tracking for a single market.
    /// </summary>
    private sealed class MarketLossState
    {
        public decimal DailyPnl { get; set; }
        public decimal WeeklyPnl { get; set; }
        public decimal MonthlyPnl { get; set; }
        public decimal CurrentDrawdown { get; set; }
        public decimal EquityHighWaterMark { get; set; }
        public decimal CurrentEquity { get; set; }
        public DateTimeOffset LastTradeTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? HaltUntil { get; set; }
        public string? HaltReason { get; set; }
    }

    private async Task SaveLossStatusAsync(int marketId, MarketLossState state, CancellationToken ct)
    {
        var status = new LossStatus
        {
            DailyPnlPercent = state.DailyPnl,
            WeeklyPnlPercent = state.WeeklyPnl,
            MonthlyPnlPercent = state.MonthlyPnl,
            DrawdownFromAthPercent = state.CurrentDrawdown,
            EquityHighWaterMark = state.EquityHighWaterMark,
            CurrentEquity = state.CurrentEquity,
            DailyLimitBreached = state.DailyPnl <= _riskConfig.LossLimits.DailyLossPercent,
            WeeklyLimitBreached = state.WeeklyPnl <= _riskConfig.LossLimits.WeeklyLossPercent,
            MonthlyLimitBreached = state.MonthlyPnl <= _riskConfig.LossLimits.MonthlyLossPercent,
            DrawdownLimitBreached = state.CurrentDrawdown <= _riskConfig.LossLimits.MaxDrawdownPercent,
            HaltUntil = state.HaltUntil,
            HaltReason = state.HaltReason
        };

        await _stateRepository.SaveLossStatusAsync(marketId, status, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var status = await _stateRepository.LoadLossStatusAsync(marketId, ct).ConfigureAwait(false);

            if (status is null)
            {
                _logger.LogDebug("No persisted loss status found for market {MarketId}", marketId);
                return false;
            }

            var state = GetOrCreateState(marketId);
            state.DailyPnl = status.DailyPnlPercent;
            state.WeeklyPnl = status.WeeklyPnlPercent;
            state.MonthlyPnl = status.MonthlyPnlPercent;
            state.CurrentDrawdown = status.DrawdownFromAthPercent;
            state.EquityHighWaterMark = status.EquityHighWaterMark;
            state.CurrentEquity = status.CurrentEquity;
            state.HaltUntil = status.HaltUntil;
            state.HaltReason = status.HaltReason;

            _logger.LogInformation(
                "Loaded persisted loss status for market {MarketId}: Daily={Daily:P2}, Weekly={Weekly:P2}",
                marketId, state.DailyPnl, state.WeeklyPnl);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted loss status for market {MarketId}", marketId);
            return false;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateLock.Dispose();
        _marketStates.Clear();
    }
}
