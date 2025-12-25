using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Trading;
using GridBot.ApiService.Models.Dashboard;
using GridBot.Core.Configuration;
using GridBot.Core.Services.Engine;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Dashboard;

public sealed class DashboardStateService : BackgroundService, IDashboardStateService
{
    private readonly ISimpleTradingEngine _engine;
    private readonly IAccountClient _accountClient;
    private readonly IRealtimeDataProvider _realtimeProvider;
    private readonly SimpleGridConfig _config;
    private readonly ILogger<DashboardStateService> _logger;

    private readonly List<AlertItem> _alerts = [];
    private readonly object _alertsLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly int _maxAlerts = 100;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    private DashboardState _currentState = DashboardState.Empty;

    public event EventHandler<DashboardState>? StateChanged;
    public DashboardState CurrentState => _currentState;

    public DashboardStateService(
        ISimpleTradingEngine engine,
        IAccountClient accountClient,
        IRealtimeDataProvider realtimeProvider,
        IOptions<SimpleGridConfig> config,
        ILogger<DashboardStateService> logger)
    {
        _engine = engine;
        _accountClient = accountClient;
        _realtimeProvider = realtimeProvider;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dashboard state service started");
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error during dashboard polling"); }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(5), ct)) return;
        try
        {
            var newState = await BuildDashboardStateAsync(ct);
            _currentState = newState;
            StateChanged?.Invoke(this, newState);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to refresh dashboard state");
        }
        finally { _refreshLock.Release(); }
    }

    public Task AddAlertAsync(AlertItem alert, CancellationToken ct = default)
    {
        lock (_alertsLock)
        {
            _alerts.Add(alert);
            while (_alerts.Count > _maxAlerts) _alerts.RemoveAt(0);
        }
        return Task.CompletedTask;
    }

    public void ClearAlerts() { lock (_alertsLock) { _alerts.Clear(); } }

    public void AcknowledgeAlert(Guid alertId)
    {
        lock (_alertsLock)
        {
            var idx = _alerts.FindIndex(a => a.Id == alertId);
            if (idx >= 0) _alerts[idx] = _alerts[idx] with { IsAcknowledged = true };
        }
    }

    public override void Dispose() { _refreshLock.Dispose(); base.Dispose(); }

    private async Task<DashboardState> BuildDashboardStateAsync(CancellationToken ct)
    {
        var engineState = _engine.State;
        var marketId = _config.MarketIndex;
        var marketIdStr = marketId.ToString();
        decimal currentPrice = 0;

        // Try realtime data first
        var realtimePrice = _realtimeProvider.GetCurrentPrice(marketIdStr);
        if (realtimePrice.HasValue && realtimePrice.Value > 0)
        {
            currentPrice = realtimePrice.Value;
        }
        else
        {
            // Fall back to REST API via account client (no direct market data client here)
            var orderBook = _realtimeProvider.GetOrderBook(marketIdStr);
            currentPrice = orderBook?.MidPrice ?? 0;
        }

        decimal equity = 0, positionSize = 0, unrealizedPnl = 0;
        var wsAccount = _realtimeProvider.GetAccount();
        if (wsAccount != null && _realtimeProvider.IsConnected)
        {
            equity = wsAccount.Collateral;
            if (wsAccount.Positions.TryGetValue(marketIdStr, out var p))
            {
                positionSize = p.Size;
                unrealizedPnl = p.UnrealizedPnl;
            }
        }
        else
        {
            var account = await _accountClient.GetAccountAsync(ct);
            equity = account.Collateral;
            if (account.Positions.TryGetValue(marketIdStr, out var pos))
            {
                positionSize = pos.Size;
                unrealizedPnl = pos.UnrealizedPnl;
            }
        }

        List<AlertItem> alertsCopy;
        lock (_alertsLock) { alertsCopy = [.. _alerts]; }

        return new DashboardState
        {
            TradingState = engineState.State.ToString(),
            StateStartedAt = DateTimeOffset.UtcNow,
            TrendState = "Neutral",
            MarketId = marketId,
            RecoveryPhase = "None",
            OperationalCapacity = _engine.IsRunning ? 100 : 0,
            CurrentPrice = currentPrice,
            PositionSize = positionSize,
            Equity = equity,
            UnrealizedPnl = unrealizedPnl,
            UnrealizedPnlPercent = equity > 0 ? (unrealizedPnl / equity) * 100 : 0,
            GridState = new GridStateInfo
            {
                Status = engineState.State.ToString(),
                CenterPrice = currentPrice,
                GridSpacing = _config.GridSpacingPercent,
                OrdersPerSide = _config.BuyLevels,
                RealizedPnl = engineState.TodayPnlUsdc,
                Levels = engineState.Levels.Select((l, i) => new GridLevelInfo
                {
                    Price = l.Price, IsBid = l.IsBuy, LevelIndex = i, Size = l.Size,
                    Status = l.OrderId.HasValue ? "Active" : "Pending"
                }).ToList()
            },
            RiskInfo = new RiskInfo { TradingAllowed = _engine.IsRunning },
            RecentAlerts = alertsCopy.OrderByDescending(a => a.Timestamp).ToList(),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }
}
