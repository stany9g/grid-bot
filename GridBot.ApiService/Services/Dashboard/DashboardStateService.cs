using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Dashboard;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.DecisionEngine;
using GridBot.ApiService.Services.Grid;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.MoonBag;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using GridBot.Lighter;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Dashboard;

/// <summary>
/// Background service that polls trading services and provides aggregated dashboard state.
/// Uses direct service injection instead of HTTP polling.
/// Thread-safe: All alert operations are protected by _alertsLock.
/// </summary>
public sealed class DashboardStateService : BackgroundService, IDashboardStateService
{
    private readonly ITradingStateService _stateService;
    private readonly ITradingDecisionEngine _decisionEngine;
    private readonly IMoonBagManager _moonBagManager;
    private readonly ILossMonitor _lossMonitor;
    private readonly IFlashCrashDetector _flashCrashDetector;
    private readonly IGridLifecycleService _gridLifecycle;
    private readonly IMarketDataService _marketDataService;
    private readonly ILighterQueryClient _queryClient;
    private readonly IRiskConfiguration _riskConfig;
    private readonly LighterOptions _lighterOptions;
    private readonly ILogger<DashboardStateService> _logger;

    private readonly List<AlertItem> _alerts = [];
    private readonly object _alertsLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly int _maxAlerts = 100;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    private DashboardState _currentState = DashboardState.Empty;
    private string? _previousTradingState;
    private string? _previousRecoveryPhase;

    public event EventHandler<DashboardState>? StateChanged;
    public DashboardState CurrentState => _currentState;

    public DashboardStateService(
        ITradingStateService stateService,
        ITradingDecisionEngine decisionEngine,
        IMoonBagManager moonBagManager,
        ILossMonitor lossMonitor,
        IFlashCrashDetector flashCrashDetector,
        IGridLifecycleService gridLifecycle,
        IMarketDataService marketDataService,
        ILighterQueryClient queryClient,
        IRiskConfiguration riskConfig,
        IOptions<LighterOptions> lighterOptions,
        ILogger<DashboardStateService> logger)
    {
        _stateService = stateService;
        _decisionEngine = decisionEngine;
        _moonBagManager = moonBagManager;
        _lossMonitor = lossMonitor;
        _flashCrashDetector = flashCrashDetector;
        _gridLifecycle = gridLifecycle;
        _marketDataService = marketDataService;
        _queryClient = queryClient;
        _riskConfig = riskConfig;
        _lighterOptions = lighterOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dashboard state service started, polling every {Interval} seconds",
            _pollingInterval.TotalSeconds);

        // Initial refresh
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during dashboard polling cycle");
            }
        }

        _logger.LogInformation("Dashboard state service stopped");
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            _logger.LogWarning("Refresh lock timeout, skipping refresh");
            return;
        }

        try
        {
            var newState = await BuildDashboardStateAsync(ct);
            UpdateState(newState);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to refresh dashboard state");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task AddAlertAsync(AlertItem alert, CancellationToken ct = default)
    {
        List<AlertItem> alertsCopy;
        lock (_alertsLock)
        {
            _alerts.Add(alert);

            // Trim old alerts if over limit (remove oldest first)
            while (_alerts.Count > _maxAlerts)
            {
                _alerts.RemoveAt(0);
            }

            alertsCopy = [.. _alerts];
        }

        // Update state with new alerts (outside lock to minimize lock duration)
        _currentState = _currentState with
        {
            RecentAlerts = alertsCopy.OrderByDescending(a => a.Timestamp).ToList()
        };

        OnStateChanged(_currentState);
        return Task.CompletedTask;
    }

    public void ClearAlerts()
    {
        lock (_alertsLock)
        {
            _alerts.Clear();
        }

        _currentState = _currentState with { RecentAlerts = [] };
        OnStateChanged(_currentState);
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        List<AlertItem>? alertsCopy = null;
        lock (_alertsLock)
        {
            var alertIndex = _alerts.FindIndex(a => a.Id == alertId);
            if (alertIndex >= 0)
            {
                _alerts[alertIndex] = _alerts[alertIndex] with { IsAcknowledged = true };
                alertsCopy = [.. _alerts];
            }
        }

        if (alertsCopy is not null)
        {
            _currentState = _currentState with
            {
                RecentAlerts = alertsCopy.OrderByDescending(a => a.Timestamp).ToList()
            };

            OnStateChanged(_currentState);
        }
    }

    public override void Dispose()
    {
        _refreshLock.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Returns a thread-safe copy of the alerts list.
    /// </summary>
    private List<AlertItem> GetAlertsCopy()
    {
        lock (_alertsLock)
        {
            return [.. _alerts];
        }
    }

    private async Task<DashboardState> BuildDashboardStateAsync(CancellationToken ct)
    {
        var marketId = _riskConfig.MarketId;
        var accountIndex = _lighterOptions.AccountIndex;

        // Gather all data in parallel
        var stateInfo = (
            State: _stateService.CurrentState,
            TrendState: _stateService.CurrentTrendState,
            StateStartedAt: _stateService.StateStartedAt
        );

        var gridTask = _gridLifecycle.GetCurrentGridStateAsync(marketId, ct);
        var moonBagTask = _moonBagManager.GetMoonBagStatusAsync(marketId, ct);
        var lossTask = _lossMonitor.GetCurrentLossStatusAsync(marketId, ct);
        var flashCrashTask = _flashCrashDetector.CheckForFlashCrashAsync(marketId, ct);
        var priceTask = _marketDataService.GetCurrentPriceAsync(marketId, ct);
        var accountTask = _queryClient.GetAccountAsync(accountIndex, ct);

        await Task.WhenAll(gridTask, moonBagTask, lossTask, flashCrashTask, priceTask, accountTask);

        var gridState = await gridTask;
        var moonBagStatus = await moonBagTask;
        var lossStatus = await lossTask;
        var flashCrashStatus = await flashCrashTask;
        var currentPrice = await priceTask;
        var account = await accountTask;

        // Get decision engine metrics
        var recoveryPhase = _decisionEngine.GetCurrentRecoveryPhase(marketId);
        var positionMultiplier = _decisionEngine.GetEffectivePositionMultiplier(marketId);
        var spreadMultiplier = _decisionEngine.GetEffectiveSpreadMultiplier(marketId);
        var consecutiveTimeouts = _decisionEngine.GetConsecutiveTimeoutCount(marketId);
        var lastDecisionResult = _decisionEngine.GetLastDecisionResult(marketId);

        // Parse account data
        decimal.TryParse(account.Collateral, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var equity);
        decimal.TryParse(account.AvailableBalance, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var availableBalance);

        // Get position
        var position = account.Positions.FirstOrDefault(p => p.MarketId == marketId);
        decimal positionSize = 0;
        decimal unrealizedPnl = 0;
        if (position != null)
        {
            decimal.TryParse(position.PositionSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out positionSize);
            decimal.TryParse(position.UnrealizedPnl, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out unrealizedPnl);
        }

        // Calculate operational capacity
        var operationalCapacity = stateInfo.State switch
        {
            TradingState.Active => 100,
            TradingState.Degraded_Bootstrap => 50,
            TradingState.Degraded_SkewCorrection => 75,
            TradingState.Degraded_HighVolatility => 60,
            TradingState.Degraded_LowLiquidity => 70,
            TradingState.Degraded_ProtectiveMode => 10,
            TradingState.Recovering => (int)(positionMultiplier * 100),
            _ => 50
        };

        // Build grid state info
        GridStateInfo? gridStateInfo = null;
        if (gridState != null)
        {
            gridStateInfo = new GridStateInfo
            {
                Status = gridState.Status.ToString(),
                CenterPrice = gridState.Parameters?.CenterPrice ?? 0,
                GridSpacing = gridState.Parameters?.GridSpacing ?? 0,
                TotalWidth = gridState.Parameters?.TotalWidth ?? 0,
                UpperBound = gridState.Parameters?.UpperBound ?? 0,
                LowerBound = gridState.Parameters?.LowerBound ?? 0,
                OrdersPerSide = gridState.Parameters?.OrdersPerSide ?? 0,
                TotalFills = gridState.TotalFills,
                ShiftCount = gridState.ShiftCount,
                RealizedPnl = 0, // Not available from grid state
                Levels = gridState.Levels.Select(l => new GridLevelInfo
                {
                    Price = l.Price,
                    IsBid = l.IsBid,
                    LevelIndex = l.LevelIndex,
                    Size = l.Size,
                    Status = l.Status.ToString()
                }).ToList()
            };
        }

        // Build trend info from last decision result
        TrendInfo? trendInfo = null;
        if (lastDecisionResult?.TrendResult != null)
        {
            var trend = lastDecisionResult.TrendResult.TrendAnalysis;
            var inventory = lastDecisionResult.TrendResult.InventoryAnalysis;
            trendInfo = new TrendInfo
            {
                CurrentState = trend.CurrentState.ToString(),
                ProposedState = trend.ProposedState.ToString(),
                ConfirmationRequired = trend.ConfirmationRequired,
                Ema20 = trend.Ema20,
                Ema50 = trend.Ema50,
                Adx = trend.Adx,
                TargetSkew = trend.TargetSkew,
                CurrentSkew = inventory.CurrentSkew,
                RebalanceDelta = inventory.RebalanceDelta,
                RebalanceNeeded = inventory.RebalanceNeeded,
                CorrectionDirection = inventory.CorrectionDirection.ToString(),
                InCooldown = trend.InCooldown
            };
        }

        // Build decision cycle info
        DecisionCycleInfo? cycleInfo = null;
        if (lastDecisionResult != null)
        {
            cycleInfo = new DecisionCycleInfo
            {
                LastCycleTime = lastDecisionResult.Timestamp,
                CycleTimeMs = (int)lastDecisionResult.ExecutionDuration.TotalMilliseconds,
                DataCollectionTimeMs = 0, // Not tracked separately
                OrdersPlaced = lastDecisionResult.OrdersPlaced,
                OrdersCancelled = lastDecisionResult.OrdersCancelled,
                CyclesPerMinute = 0, // Could be calculated from history
                SkippedCycles = 0,
                TimeoutCount = consecutiveTimeouts,
                ErrorCount = 0
            };
        }

        // Build risk info
        var riskInfo = new RiskInfo
        {
            TradingAllowed = !lossStatus.AnyLimitBreached,
            BuysBlocked = flashCrashStatus.RequiredAction == FlashCrashAction.PauseBuys ||
                          flashCrashStatus.RequiredAction == FlashCrashAction.PauseAll,
            SellsBlocked = flashCrashStatus.RequiredAction == FlashCrashAction.PauseAll,
            DailyPnlPercent = lossStatus.Rolling24hPnlPercent,
            WeeklyPnlPercent = lossStatus.Rolling7dPnlPercent,
            MonthlyPnlPercent = lossStatus.Rolling30dPnlPercent,
            DrawdownPercent = lossStatus.DrawdownFromAthPercent,
            AnyLimitBreached = lossStatus.AnyLimitBreached,
            HaltReason = lossStatus.HaltReason,
            HaltUntil = lossStatus.HaltUntil,
            FlashCrashActive = flashCrashStatus.IsInProtection,
            FlashCrashSeverity = flashCrashStatus.Severity.ToString(),
            FlashCrashAction = flashCrashStatus.RequiredAction.ToString(),
            FlashCrashProtectionUntil = flashCrashStatus.ProtectionUntil,
            CrashCount24h = _flashCrashDetector.GetCrashCount24h(marketId),
            ActiveWarnings = []
        };

        // Build moon bag info
        var moonBagInfo = new MoonBagInfo
        {
            State = moonBagStatus.State.ToString(),
            LockedQuantity = moonBagStatus.LockedQuantity,
            HighWatermarkPrice = moonBagStatus.HighWatermarkPrice,
            TrailingStopPrice = moonBagStatus.TrailingStopPrice,
            CurrentProfitPercent = moonBagStatus.CurrentProfitPercent,
            MaxPositionAchieved = moonBagStatus.MaxPositionAchieved,
            HasActiveStopOrder = moonBagStatus.HasActiveStopOrder,
            CurrentTier = moonBagStatus.CurrentTier.ToString(),
            StateReason = moonBagStatus.StateReason
        };

        var state = new DashboardState
        {
            TradingState = stateInfo.State.ToString(),
            StateStartedAt = stateInfo.StateStartedAt,
            TrendState = stateInfo.TrendState.ToString(),
            Uptime = DateTimeOffset.UtcNow - stateInfo.StateStartedAt,
            MarketId = marketId,
            RecoveryPhase = recoveryPhase.ToString(),
            PositionMultiplier = positionMultiplier,
            SpreadMultiplier = spreadMultiplier,
            ConsecutiveTimeouts = consecutiveTimeouts,
            OperationalCapacity = operationalCapacity,
            CurrentPrice = currentPrice,
            PositionSize = positionSize,
            Equity = equity,
            UnrealizedPnl = unrealizedPnl,
            UnrealizedPnlPercent = equity > 0 ? (unrealizedPnl / equity) * 100 : 0,
            GridState = gridStateInfo,
            RiskInfo = riskInfo,
            TrendInfo = trendInfo,
            MoonBagInfo = moonBagInfo,
            CycleInfo = cycleInfo,
            RecentAlerts = GetAlertsCopy().OrderByDescending(a => a.Timestamp).ToList(),
            LastUpdated = DateTimeOffset.UtcNow
        };

        return state;
    }

    private void UpdateState(DashboardState newState)
    {
        // Check for state changes that should trigger alerts
        CheckForStateChangeAlerts(newState);

        _currentState = newState;
        _previousTradingState = newState.TradingState;
        _previousRecoveryPhase = newState.RecoveryPhase;

        OnStateChanged(newState);
    }

    private void CheckForStateChangeAlerts(DashboardState newState)
    {
        // Trading state change
        if (_previousTradingState is not null &&
            _previousTradingState != newState.TradingState)
        {
            var severity = newState.TradingState.Contains("Protective")
                ? Models.Dashboard.AlertSeverity.Critical
                : newState.TradingState.Contains("Degraded")
                    ? Models.Dashboard.AlertSeverity.Warning
                    : Models.Dashboard.AlertSeverity.Info;

            var alert = new AlertItem
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Severity = severity,
                EventType = AlertEventTypes.StateChange,
                Title = "Trading State Changed",
                Message = $"State changed: {_previousTradingState} -> {newState.TradingState}",
                Data = new Dictionary<string, object>
                {
                    ["previous_state"] = _previousTradingState,
                    ["new_state"] = newState.TradingState,
                    ["market_id"] = newState.MarketId,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")
                }
            };

            _ = AddAlertAsync(alert);
        }

        // Recovery phase change
        if (_previousRecoveryPhase is not null &&
            _previousRecoveryPhase != newState.RecoveryPhase &&
            newState.RecoveryPhase != "None")
        {
            var alert = AlertItem.Info(
                AlertEventTypes.RecoveryPhase,
                "Recovery Phase Changed",
                $"Recovery phase: {_previousRecoveryPhase} -> {newState.RecoveryPhase}",
                new Dictionary<string, object>
                {
                    ["previous_phase"] = _previousRecoveryPhase,
                    ["new_phase"] = newState.RecoveryPhase,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")
                });

            _ = AddAlertAsync(alert);
        }

        // Flash crash detection
        if (newState.RiskInfo?.FlashCrashActive == true &&
            _currentState.RiskInfo?.FlashCrashActive != true)
        {
            var alert = AlertItem.Critical(
                AlertEventTypes.FlashCrash,
                "Flash Crash Detected",
                $"Flash crash detected: {newState.RiskInfo.FlashCrashSeverity}. Action: {newState.RiskInfo.FlashCrashAction}",
                new Dictionary<string, object>
                {
                    ["severity"] = newState.RiskInfo.FlashCrashSeverity,
                    ["action"] = newState.RiskInfo.FlashCrashAction,
                    ["protection_until"] = newState.RiskInfo.FlashCrashProtectionUntil?.ToString("o") ?? "unknown",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")
                });

            _ = AddAlertAsync(alert);
        }

        // Loss limit breach
        if (newState.RiskInfo?.AnyLimitBreached == true &&
            _currentState.RiskInfo?.AnyLimitBreached != true)
        {
            var alert = AlertItem.Critical(
                AlertEventTypes.LossLimit,
                "Loss Limit Breached",
                $"Loss limit breached. Daily: {newState.RiskInfo.DailyPnlPercent:F2}%, Weekly: {newState.RiskInfo.WeeklyPnlPercent:F2}%",
                new Dictionary<string, object>
                {
                    ["daily_pnl"] = newState.RiskInfo.DailyPnlPercent,
                    ["weekly_pnl"] = newState.RiskInfo.WeeklyPnlPercent,
                    ["monthly_pnl"] = newState.RiskInfo.MonthlyPnlPercent,
                    ["drawdown"] = newState.RiskInfo.DrawdownPercent,
                    ["halt_reason"] = newState.RiskInfo.HaltReason ?? "unknown",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")
                });

            _ = AddAlertAsync(alert);
        }

        // Timeout warning
        if (newState.ConsecutiveTimeouts >= 3 &&
            _currentState.ConsecutiveTimeouts < 3)
        {
            var alert = AlertItem.Warning(
                AlertEventTypes.TimeoutWarning,
                "Consecutive Timeouts",
                $"Warning: {newState.ConsecutiveTimeouts} consecutive API timeouts detected",
                new Dictionary<string, object>
                {
                    ["count"] = newState.ConsecutiveTimeouts,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")
                });

            _ = AddAlertAsync(alert);
        }
    }

    private void OnStateChanged(DashboardState state)
    {
        StateChanged?.Invoke(this, state);
    }
}
