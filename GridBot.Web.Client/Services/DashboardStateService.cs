using System.Collections.Concurrent;
using GridBot.Web.Client.Models;
using Microsoft.Extensions.Logging;

namespace GridBot.Web.Client.Services;

/// <summary>
/// Service that aggregates trading data and manages dashboard state.
/// Runs in the browser via WebAssembly.
/// </summary>
public sealed class DashboardStateService : IDashboardStateService
{
    private readonly TradingApiClient _tradingApiClient;
    private readonly ILogger<DashboardStateService> _logger;

    private readonly ConcurrentQueue<AlertItem> _alerts = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly int _maxAlerts = 100;

    private DashboardState _currentState = DashboardState.Empty;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string? _previousTradingState;
    private string? _previousRecoveryPhase;

    public event EventHandler<DashboardState>? StateChanged;
    public DashboardState CurrentState => _currentState;
    public bool IsPolling => _pollingTask is not null && !_pollingTask.IsCompleted;

    public DashboardStateService(
        TradingApiClient tradingApiClient,
        ILogger<DashboardStateService> logger)
    {
        _tradingApiClient = tradingApiClient;
        _logger = logger;
    }

    public async Task StartPollingAsync(CancellationToken ct = default)
    {
        if (IsPolling)
        {
            _logger.LogDebug("Polling already started");
            return;
        }

        _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollingTask = PollForUpdatesAsync(_pollingCts.Token);

        _logger.LogInformation("Dashboard state polling started");

        // Initial refresh
        await RefreshAsync(ct);
    }

    public async Task StopPollingAsync()
    {
        if (_pollingCts is not null)
        {
            await _pollingCts.CancelAsync();
            _pollingCts.Dispose();
            _pollingCts = null;
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            _pollingTask = null;
        }

        _logger.LogInformation("Dashboard state polling stopped");
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
            var newState = await FetchDashboardStateAsync(ct);
            await UpdateStateAsync(newState, ct);
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
        _alerts.Enqueue(alert);

        // Trim old alerts if over limit
        while (_alerts.Count > _maxAlerts && _alerts.TryDequeue(out _))
        {
            // Continue dequeuing
        }

        // Update state with new alerts
        _currentState = _currentState with
        {
            RecentAlerts = _alerts.ToList().OrderByDescending(a => a.Timestamp).ToList()
        };

        OnStateChanged(_currentState);
        return Task.CompletedTask;
    }

    public void ClearAlerts()
    {
        while (_alerts.TryDequeue(out _))
        {
            // Clear all
        }

        _currentState = _currentState with { RecentAlerts = [] };
        OnStateChanged(_currentState);
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        var alerts = _alerts.ToList();
        var alertIndex = alerts.FindIndex(a => a.Id == alertId);

        if (alertIndex >= 0)
        {
            alerts[alertIndex] = alerts[alertIndex] with { IsAcknowledged = true };

            // Rebuild the queue
            while (_alerts.TryDequeue(out _)) { }
            foreach (var alert in alerts)
            {
                _alerts.Enqueue(alert);
            }

            _currentState = _currentState with
            {
                RecentAlerts = _alerts.ToList().OrderByDescending(a => a.Timestamp).ToList()
            };

            OnStateChanged(_currentState);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopPollingAsync();
        _refreshLock.Dispose();
    }

    private async Task PollForUpdatesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during polling cycle");
            }
        }
    }

    private async Task<DashboardState> FetchDashboardStateAsync(CancellationToken ct)
    {
        // Fetch comprehensive dashboard data in a single call
        var response = await _tradingApiClient.GetDashboardAsync(ct);

        if (response is null)
        {
            _logger.LogWarning("Dashboard API returned null response");
            return DashboardState.Empty with
            {
                RecentAlerts = _alerts.ToList().OrderByDescending(a => a.Timestamp).ToList(),
                LastUpdated = DateTimeOffset.UtcNow
            };
        }

        // Map API response to dashboard state
        var state = new DashboardState
        {
            TradingState = response.TradingState,
            StateStartedAt = response.StateStartedAt,
            TrendState = response.TrendState,
            Uptime = response.Uptime,
            MarketId = response.MarketId,
            RecoveryPhase = response.RecoveryPhase,
            PositionMultiplier = response.PositionMultiplier,
            SpreadMultiplier = response.SpreadMultiplier,
            ConsecutiveTimeouts = response.ConsecutiveTimeouts,
            OperationalCapacity = response.OperationalCapacity,
            CurrentPrice = response.CurrentPrice,
            PositionSize = response.PositionSize,
            Equity = response.Equity,
            UnrealizedPnl = response.UnrealizedPnl,
            UnrealizedPnlPercent = response.UnrealizedPnlPercent,
            GridState = response.Grid is not null ? new GridStateInfo
            {
                Status = response.Grid.Status,
                CenterPrice = response.Grid.CenterPrice,
                GridSpacing = response.Grid.GridSpacing,
                TotalWidth = response.Grid.TotalWidth,
                UpperBound = response.Grid.UpperBound,
                LowerBound = response.Grid.LowerBound,
                OrdersPerSide = response.Grid.OrdersPerSide,
                TotalFills = response.Grid.TotalFills,
                ShiftCount = response.Grid.ShiftCount,
                Levels = response.Grid.Levels.Select(l => new GridLevelInfo
                {
                    Price = l.Price,
                    IsBid = l.IsBid,
                    LevelIndex = l.LevelIndex,
                    Size = l.Size,
                    Status = l.Status
                }).ToList()
            } : null,
            RiskInfo = response.Risk is not null ? new RiskInfo
            {
                TradingAllowed = response.Risk.TradingAllowed,
                BuysBlocked = response.Risk.BuysBlocked,
                SellsBlocked = response.Risk.SellsBlocked,
                DailyPnlPercent = response.Risk.DailyPnlPercent,
                WeeklyPnlPercent = response.Risk.WeeklyPnlPercent,
                MonthlyPnlPercent = response.Risk.MonthlyPnlPercent,
                DrawdownPercent = response.Risk.DrawdownPercent,
                AnyLimitBreached = response.Risk.AnyLimitBreached,
                HaltReason = response.Risk.HaltReason,
                HaltUntil = response.Risk.HaltUntil,
                FlashCrashActive = response.Risk.FlashCrashActive,
                FlashCrashSeverity = response.Risk.FlashCrashSeverity,
                FlashCrashAction = response.Risk.FlashCrashAction,
                FlashCrashProtectionUntil = response.Risk.FlashCrashProtectionUntil,
                CrashCount24h = response.Risk.CrashCount24h,
                ActiveWarnings = response.Risk.ActiveWarnings
            } : null,
            TrendInfo = response.Trend is not null ? new TrendInfo
            {
                CurrentState = response.Trend.CurrentState,
                ProposedState = response.Trend.ProposedState,
                ConfirmationRequired = response.Trend.ConfirmationRequired,
                Ema20 = response.Trend.Ema20,
                Ema50 = response.Trend.Ema50,
                Adx = response.Trend.Adx,
                TargetSkew = response.Trend.TargetSkew,
                CurrentSkew = response.Trend.CurrentSkew,
                RebalanceDelta = response.Trend.RebalanceDelta,
                RebalanceNeeded = response.Trend.RebalanceNeeded,
                CorrectionDirection = response.Trend.CorrectionDirection,
                InCooldown = response.Trend.InCooldown
            } : null,
            MoonBagInfo = response.MoonBag is not null ? new MoonBagInfo
            {
                State = response.MoonBag.State,
                LockedQuantity = response.MoonBag.LockedQuantity,
                HighWatermarkPrice = response.MoonBag.HighWatermarkPrice,
                TrailingStopPrice = response.MoonBag.TrailingStopPrice,
                CurrentProfitPercent = response.MoonBag.CurrentProfitPercent,
                MaxPositionAchieved = response.MoonBag.MaxPositionAchieved,
                HasActiveStopOrder = response.MoonBag.HasActiveStopOrder
            } : null,
            CycleInfo = response.Cycle is not null ? new DecisionCycleInfo
            {
                LastCycleTime = response.Cycle.LastCycleTime,
                CycleTimeMs = response.Cycle.CycleTimeMs,
                DataCollectionTimeMs = response.Cycle.DataCollectionTimeMs,
                OrdersPlaced = response.Cycle.OrdersPlaced,
                OrdersCancelled = response.Cycle.OrdersCancelled
            } : null,
            RecentAlerts = _alerts.ToList().OrderByDescending(a => a.Timestamp).ToList(),
            LastUpdated = DateTimeOffset.UtcNow
        };

        return state;
    }

    private Task UpdateStateAsync(DashboardState newState, CancellationToken ct)
    {
        // Check for state changes that should trigger alerts
        CheckForStateChangeAlerts(newState);

        _currentState = newState;
        _previousTradingState = newState.TradingState;
        _previousRecoveryPhase = newState.RecoveryPhase;

        OnStateChanged(newState);
        return Task.CompletedTask;
    }

    private void CheckForStateChangeAlerts(DashboardState newState)
    {
        // Trading state change
        if (_previousTradingState is not null &&
            _previousTradingState != newState.TradingState)
        {
            var severity = newState.TradingState.Contains("Protective")
                ? AlertSeverity.Critical
                : newState.TradingState.Contains("Degraded")
                    ? AlertSeverity.Warning
                    : AlertSeverity.Info;

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
