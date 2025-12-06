using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Persistence;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors P&amp;L and loss limits for trading protection using rolling windows.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class LossMonitor : ILossMonitor, IDisposable
{
    private readonly ILogger<LossMonitor> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingState;
    private readonly IRiskEventLogger _eventLogger;
    private readonly IStateRepository _stateRepository;

    private readonly ConcurrentDictionary<int, RollingLossState> _marketStates = new();
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
    public async Task<RollingLossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // BUG FIX: Take snapshot under lock to prevent race conditions
        // Reading TradeHistory without lock can cause InvalidOperationException
        List<TradeRecord> tradesCopy;
        decimal currentEquity, equityHwm, currentDrawdown;
        DateTimeOffset? haltUntil;
        string? haltReason;

        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);
            tradesCopy = [.. state.TradeHistory]; // defensive copy using collection expression
            currentEquity = state.CurrentEquity;
            equityHwm = state.EquityHighWaterMark;
            currentDrawdown = state.CurrentDrawdown;
            haltUntil = state.HaltUntil;
            haltReason = state.HaltReason;
        }
        finally
        {
            _updateLock.Release();
        }

        // Calculate outside lock using snapshots
        var limits = _riskConfig.LossLimits;
        var pnl24h = CalculateRollingPnlFromSnapshot(tradesCopy, TimeSpan.FromHours(24));
        var pnl7d = CalculateRollingPnlFromSnapshot(tradesCopy, TimeSpan.FromDays(7));
        var pnl30d = CalculateRollingPnlFromSnapshot(tradesCopy, TimeSpan.FromDays(30));
        var tradesIn24h = CountTradesInWindowFromSnapshot(tradesCopy, TimeSpan.FromHours(24));
        var tradesIn7d = CountTradesInWindowFromSnapshot(tradesCopy, TimeSpan.FromDays(7));

        return new RollingLossStatus
        {
            Rolling24hPnlPercent = pnl24h,
            Rolling7dPnlPercent = pnl7d,
            Rolling30dPnlPercent = pnl30d,
            DrawdownFromAthPercent = currentDrawdown,
            EquityHighWaterMark = equityHwm,
            CurrentEquity = currentEquity,
            TradesIn24h = tradesIn24h,
            TradesIn7d = tradesIn7d,
            Rolling24hBreached = pnl24h <= limits.Rolling24HourLossPercent,
            Rolling7dBreached = pnl7d <= limits.Rolling7DayLossPercent,
            Rolling30dBreached = pnl30d <= limits.Rolling30DayLossPercent,
            DrawdownBreached = currentDrawdown <= limits.MaxDrawdownPercent,
            HaltUntil = haltUntil,
            HaltReason = haltReason
        };
    }

    /// <summary>
    /// Maximum single trade P&L percentage (+/- 50%).
    /// Any trade exceeding this is considered suspicious and will be capped.
    /// </summary>
    private const decimal MaxSingleTradePnl = 50m;

    /// <inheritdoc />
    public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default)
    {
        // BUG FIX: Sanity bounds check - no single trade should be > +/- 50%
        // This prevents manipulation or erroneous data from skewing calculations
        if (Math.Abs(pnlPercent) > MaxSingleTradePnl)
        {
            _logger.LogCritical(
                "SUSPICIOUS TRADE P&L: {PnlPercent:F2}% exceeds bounds (+/- {Max}%). Capping for safety. Market: {MarketId}",
                pnlPercent, MaxSingleTradePnl, marketId);

            var riskEvent = RiskEvent.Create(
                "PNL-BOUNDS",
                AlertSeverity.Critical,
                $"Trade P&L {pnlPercent:F2}% exceeds safe bounds (+/- {MaxSingleTradePnl}%)",
                "Value capped, manual investigation required",
                pnlPercent,
                MaxSingleTradePnl);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            // Cap at bounds
            pnlPercent = Math.Sign(pnlPercent) * MaxSingleTradePnl;
        }

        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);

            // Create a trade record
            var trade = TradeRecord.Create(marketId, pnlPercent, state.CurrentEquity);
            state.TradeHistory.Add(trade);

            _logger.LogDebug(
                "Recorded trade P&L for market {MarketId}: {PnlPercent}%. 24h: {Pnl24h}%, 7d: {Pnl7d}%",
                marketId, pnlPercent,
                CalculateRollingPnl(state, TimeSpan.FromHours(24)),
                CalculateRollingPnl(state, TimeSpan.FromDays(7)));

            // Persist trade record to Redis
            await _stateRepository.SaveTradeRecordAsync(marketId, trade, ct).ConfigureAwait(false);

            // Check for single trade loss limit
            if (pnlPercent <= _riskConfig.LossLimits.SingleTradeLossPercent)
            {
                var riskEvent = RiskEvent.Create(
                    "LOSS-001",
                    AlertSeverity.High,
                    $"Single trade loss of {pnlPercent:F2}% exceeds limit of {_riskConfig.LossLimits.SingleTradeLossPercent}%",
                    "Alert operator, no halt",
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
    public async Task RecordTradeAsync(int marketId, TradeRecord trade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trade);

        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);
            state.TradeHistory.Add(trade);

            // Persist trade record to Redis
            await _stateRepository.SaveTradeRecordAsync(marketId, trade, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Recorded explicit trade for market {MarketId}: {PnlPercent}%",
                marketId, trade.PnlPercent);

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

            // Persist rolling loss state
            await SaveRollingLossStateAsync(marketId, state, ct).ConfigureAwait(false);

            await CheckAndTriggerLimitsAsync(marketId, state, ct).ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordEquitySnapshotAsync(int marketId, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = GetOrCreateState(marketId);

            if (state.CurrentEquity <= 0)
            {
                _logger.LogDebug("Skipping equity snapshot for market {MarketId}: no equity recorded", marketId);
                return;
            }

            var snapshot = EquitySnapshot.Create(marketId, state.CurrentEquity);
            state.EquitySnapshots.Add(snapshot);

            // Persist snapshot to Redis
            await _stateRepository.SaveEquitySnapshotAsync(marketId, snapshot, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Recorded equity snapshot for market {MarketId}: {Equity:F2} USD",
                marketId, state.CurrentEquity);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<decimal> GetRollingPnlAsync(int marketId, TimeSpan window, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = GetOrCreateState(marketId);
        return Task.FromResult(CalculateRollingPnl(state, window));
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

            // Check if we can auto-clear based on recovery conditions
            if (await CanAutoClearHaltAsync(marketId, state, ct).ConfigureAwait(false))
            {
                state.HaltUntil = null;
                state.HaltReason = null;
                state.HaltStarted = null;
                _logger.LogInformation("Halt period expired and recovery conditions met for market {MarketId}", marketId);
            }
            else
            {
                // Extend halt - conditions not met
                return false;
            }
        }

        // Check rolling limits
        var limits = _riskConfig.LossLimits;

        var pnl24h = CalculateRollingPnl(state, TimeSpan.FromHours(24));
        if (pnl24h <= limits.Rolling24HourLossPercent)
        {
            return false;
        }

        var pnl7d = CalculateRollingPnl(state, TimeSpan.FromDays(7));
        if (pnl7d <= limits.Rolling7DayLossPercent)
        {
            return false;
        }

        var pnl30d = CalculateRollingPnl(state, TimeSpan.FromDays(30));
        if (pnl30d <= limits.Rolling30DayLossPercent)
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
    public void ClearHalt(int marketId, string? operatorId = null, string? reason = null)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            var wasDrawdownHalt = state.HaltReason == "MaxDrawdown";

            state.HaltUntil = null;
            state.HaltReason = null;
            state.HaltStarted = null;

            if (wasDrawdownHalt)
            {
                _logger.LogWarning(
                    "Manual halt clear for DRAWDOWN by {OperatorId}: {Reason}. Trading at 50% capacity recommended.",
                    operatorId ?? "Unknown", reason ?? "No reason provided");
            }
            else
            {
                _logger.LogWarning(
                    "Manually cleared halt for market {MarketId} by {OperatorId}: {Reason}",
                    marketId, operatorId ?? "Unknown", reason ?? "No reason provided");
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Load rolling loss state
            var lossState = await _stateRepository.LoadRollingLossStateAsync(marketId, ct).ConfigureAwait(false);

            if (lossState is null)
            {
                _logger.LogDebug("No persisted rolling loss state found for market {MarketId}", marketId);
                return false;
            }

            var state = GetOrCreateState(marketId);
            state.CurrentEquity = lossState.CurrentEquity;
            state.EquityHighWaterMark = lossState.EquityHighWaterMark;
            state.CurrentDrawdown = lossState.CurrentDrawdown;
            state.HaltUntil = lossState.HaltUntil;
            state.HaltReason = lossState.HaltReason;
            state.HaltStarted = lossState.HaltStarted;

            // Load trade history from Redis
            var retentionDays = _riskConfig.LossLimits.TradeRecordRetentionDays;
            var since = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var trades = await _stateRepository.LoadTradeRecordsAsync(marketId, since, ct).ConfigureAwait(false);
            state.TradeHistory = [.. trades];

            // Load equity snapshots
            var snapshots = await _stateRepository.LoadEquitySnapshotsAsync(marketId, since, ct).ConfigureAwait(false);
            state.EquitySnapshots = [.. snapshots];

            _logger.LogInformation(
                "Loaded persisted rolling loss state for market {MarketId}: 24h={Pnl24h:F2}%, Trades={TradeCount}",
                marketId,
                CalculateRollingPnl(state, TimeSpan.FromHours(24)),
                state.TradeHistory.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted rolling loss state for market {MarketId}", marketId);
            return false;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task CleanupOldRecordsAsync(int marketId, CancellationToken ct = default)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retentionDays = _riskConfig.LossLimits.TradeRecordRetentionDays;
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

            var state = GetOrCreateState(marketId);

            // Clean up in-memory trade history
            var oldTradeCount = state.TradeHistory.Count;
            state.TradeHistory.RemoveAll(t => t.Timestamp < cutoff);
            var removedTradeCount = oldTradeCount - state.TradeHistory.Count;

            // Clean up in-memory equity snapshots
            var oldSnapshotCount = state.EquitySnapshots.Count;
            state.EquitySnapshots.RemoveAll(s => s.Timestamp < cutoff);
            var removedSnapshotCount = oldSnapshotCount - state.EquitySnapshots.Count;

            // Clean up Redis
            await _stateRepository.CleanupOldTradeRecordsAsync(marketId, retentionDays, ct).ConfigureAwait(false);
            await _stateRepository.CleanupOldEquitySnapshotsAsync(marketId, retentionDays, ct).ConfigureAwait(false);

            if (removedTradeCount > 0 || removedSnapshotCount > 0)
            {
                _logger.LogInformation(
                    "Cleaned up old records for market {MarketId}: {TradeCount} trades, {SnapshotCount} snapshots removed",
                    marketId, removedTradeCount, removedSnapshotCount);
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private RollingLossState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new RollingLossState { MarketId = marketId });
    }

    private static decimal CalculateRollingPnl(RollingLossState state, TimeSpan window)
    {
        if (state.TradeHistory.Count == 0)
        {
            return 0m;
        }

        var windowStart = DateTimeOffset.UtcNow - window;
        return state.TradeHistory
            .Where(t => t.Timestamp >= windowStart)
            .Sum(t => t.PnlPercent);
    }

    private static int CountTradesInWindow(RollingLossState state, TimeSpan window)
    {
        var windowStart = DateTimeOffset.UtcNow - window;
        return state.TradeHistory.Count(t => t.Timestamp >= windowStart);
    }

    /// <summary>
    /// Calculates rolling P&L from a thread-safe snapshot of trades.
    /// </summary>
    private static decimal CalculateRollingPnlFromSnapshot(IReadOnlyList<TradeRecord> trades, TimeSpan window)
    {
        if (trades.Count == 0)
        {
            return 0m;
        }

        var windowStart = DateTimeOffset.UtcNow - window;
        return trades
            .Where(t => t.Timestamp >= windowStart)
            .Sum(t => t.PnlPercent);
    }

    /// <summary>
    /// Counts trades in a window from a thread-safe snapshot.
    /// </summary>
    private static int CountTradesInWindowFromSnapshot(IReadOnlyList<TradeRecord> trades, TimeSpan window)
    {
        var windowStart = DateTimeOffset.UtcNow - window;
        return trades.Count(t => t.Timestamp >= windowStart);
    }

    private async Task<bool> CanAutoClearHaltAsync(int marketId, RollingLossState state, CancellationToken ct)
    {
        var limits = _riskConfig.LossLimits;

        // Max drawdown never auto-clears - requires equity recovery or manual override
        if (state.HaltReason == "MaxDrawdown")
        {
            // Only clear if equity recovered to 75% of HWM
            var recoveryTarget = state.EquityHighWaterMark * 0.75m;
            if (state.CurrentEquity < recoveryTarget)
            {
                return false;
            }

            _logger.LogInformation(
                "Max drawdown recovery: equity {Current:F2} >= 75% of HWM {HWM:F2}",
                state.CurrentEquity, state.EquityHighWaterMark);
            return true;
        }

        // For rolling limits, check if metrics have improved to 50% of limit
        var pnl24h = CalculateRollingPnl(state, TimeSpan.FromHours(24));
        var pnl7d = CalculateRollingPnl(state, TimeSpan.FromDays(7));
        var pnl30d = CalculateRollingPnl(state, TimeSpan.FromDays(30));

        return state.HaltReason switch
        {
            "Rolling24hLimit" => pnl24h > limits.Rolling24HourLossPercent * 0.5m,
            "Rolling7dLimit" => pnl7d > limits.Rolling7DayLossPercent * 0.5m,
            "Rolling30dLimit" => pnl30d > limits.Rolling30DayLossPercent * 0.5m,
            _ => true
        };
    }

    private async Task CheckAndTriggerLimitsAsync(int marketId, RollingLossState state, CancellationToken ct)
    {
        var limits = _riskConfig.LossLimits;

        var pnl24h = CalculateRollingPnl(state, TimeSpan.FromHours(24));
        var pnl7d = CalculateRollingPnl(state, TimeSpan.FromDays(7));
        var pnl30d = CalculateRollingPnl(state, TimeSpan.FromDays(30));

        // Rolling 24h limit check
        if (pnl24h <= limits.Rolling24HourLossPercent && state.HaltReason != "Rolling24hLimit")
        {
            state.HaltUntil = DateTimeOffset.UtcNow.AddHours(limits.Rolling24HourRecoveryWaitHours);
            state.HaltReason = "Rolling24hLimit";
            state.HaltStarted = DateTimeOffset.UtcNow;

            var riskEvent = RiskEvent.Create(
                "LOSS-R001",
                AlertSeverity.Critical,
                $"Rolling 24h loss limit breached: {pnl24h:F2}% <= {limits.Rolling24HourLossPercent}%",
                $"Enter protective mode, minimum {limits.Rolling24HourRecoveryWaitHours}h wait",
                pnl24h,
                limits.Rolling24HourLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Rolling 24h loss limit breached").ConfigureAwait(false);

            _logger.LogCritical(
                "ROLLING 24H LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. Entering protective mode.",
                marketId, pnl24h, limits.Rolling24HourLossPercent);
        }

        // Rolling 7d limit check (more severe)
        if (pnl7d <= limits.Rolling7DayLossPercent && state.HaltReason != "Rolling7dLimit")
        {
            state.HaltUntil = DateTimeOffset.UtcNow.AddHours(limits.Rolling7DayRecoveryWaitHours);
            state.HaltReason = "Rolling7dLimit";
            state.HaltStarted = DateTimeOffset.UtcNow;

            var riskEvent = RiskEvent.Create(
                "LOSS-R002",
                AlertSeverity.Critical,
                $"Rolling 7d loss limit breached: {pnl7d:F2}% <= {limits.Rolling7DayLossPercent}%",
                $"Enter protective mode, minimum {limits.Rolling7DayRecoveryWaitHours}h wait, cancel all orders",
                pnl7d,
                limits.Rolling7DayLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Rolling 7d loss limit breached").ConfigureAwait(false);

            _logger.LogCritical(
                "ROLLING 7D LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. Entering protective mode for {Hours}h.",
                marketId, pnl7d, limits.Rolling7DayLossPercent, limits.Rolling7DayRecoveryWaitHours);
        }

        // Rolling 30d limit check (most severe, requires manual review)
        if (pnl30d <= limits.Rolling30DayLossPercent && state.HaltReason != "Rolling30dLimit")
        {
            state.HaltUntil = DateTimeOffset.UtcNow.AddHours(limits.Rolling30DayRecoveryWaitHours);
            state.HaltReason = "Rolling30dLimit";
            state.HaltStarted = DateTimeOffset.UtcNow;

            var riskEvent = RiskEvent.Create(
                "LOSS-R003",
                AlertSeverity.Critical,
                $"Rolling 30d loss limit breached: {pnl30d:F2}% <= {limits.Rolling30DayLossPercent}%",
                $"Enter protective mode, strategy review required, {limits.Rolling30DayRecoveryWaitHours}h minimum",
                pnl30d,
                limits.Rolling30DayLossPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
            await _tradingState.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Rolling 30d loss limit breached - strategy review required").ConfigureAwait(false);

            _logger.LogCritical(
                "ROLLING 30D LOSS LIMIT BREACHED for market {MarketId}: {Loss}% <= {Limit}%. STRATEGY REVIEW REQUIRED.",
                marketId, pnl30d, limits.Rolling30DayLossPercent);
        }

        // Max drawdown check - reduce position size by 75%
        if (state.CurrentDrawdown <= limits.MaxDrawdownPercent && state.HaltReason != "MaxDrawdown")
        {
            state.HaltReason = "MaxDrawdown";
            state.HaltStarted = DateTimeOffset.UtcNow;
            // No HaltUntil for drawdown - requires equity recovery or manual override

            var riskEvent = RiskEvent.Create(
                "LOSS-R004",
                AlertSeverity.Critical,
                $"Max drawdown breached: {state.CurrentDrawdown:F2}% <= {limits.MaxDrawdownPercent}%",
                $"Reduce position sizes by {limits.DrawdownPositionReductionPercent}%, NO auto-recovery",
                state.CurrentDrawdown,
                limits.MaxDrawdownPercent);

            await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

            _logger.LogCritical(
                "MAX DRAWDOWN BREACHED for market {MarketId}: {Drawdown}% <= {Limit}%. Position sizes reduced by {Reduction}%. NO AUTO-RECOVERY.",
                marketId, state.CurrentDrawdown, limits.MaxDrawdownPercent, limits.DrawdownPositionReductionPercent);
        }

        // Persist state after any limit check
        await SaveRollingLossStateAsync(marketId, state, ct).ConfigureAwait(false);
    }

    private async Task SaveRollingLossStateAsync(int marketId, RollingLossState state, CancellationToken ct)
    {
        var persistedState = new PersistedRollingLossState
        {
            MarketId = marketId,
            CurrentEquity = state.CurrentEquity,
            EquityHighWaterMark = state.EquityHighWaterMark,
            CurrentDrawdown = state.CurrentDrawdown,
            HaltUntil = state.HaltUntil,
            HaltReason = state.HaltReason,
            HaltStarted = state.HaltStarted
        };

        await _stateRepository.SaveRollingLossStateAsync(marketId, persistedState, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Internal state tracking for a single market using rolling windows.
    /// </summary>
    private sealed class RollingLossState
    {
        public int MarketId { get; init; }
        public List<TradeRecord> TradeHistory { get; set; } = [];
        public List<EquitySnapshot> EquitySnapshots { get; set; } = [];
        public decimal CurrentEquity { get; set; }
        public decimal EquityHighWaterMark { get; set; }
        public decimal CurrentDrawdown { get; set; }
        public DateTimeOffset? HaltUntil { get; set; }
        public string? HaltReason { get; set; }
        public DateTimeOffset? HaltStarted { get; set; }
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

/// <summary>
/// Persisted state for rolling loss monitor.
/// Excludes trade history which is stored separately.
/// </summary>
public sealed class PersistedRollingLossState
{
    public int MarketId { get; init; }
    public decimal CurrentEquity { get; init; }
    public decimal EquityHighWaterMark { get; init; }
    public decimal CurrentDrawdown { get; init; }
    public DateTimeOffset? HaltUntil { get; init; }
    public string? HaltReason { get; init; }
    public DateTimeOffset? HaltStarted { get; init; }
}
