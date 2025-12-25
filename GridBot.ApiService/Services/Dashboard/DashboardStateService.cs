using GridBot.ApiService.Models.Dashboard;
using GridBot.Core.Configuration;
using GridBot.Core.Services.Engine;
using GridBot.Lighter;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Dashboard;

public sealed class DashboardStateService : BackgroundService, IDashboardStateService
{
    private readonly ISimpleTradingEngine _engine;
    private readonly ILighterQueryClient _queryClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly SimpleGridConfig _config;
    private readonly LighterOptions _lighterOptions;
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
        ILighterQueryClient queryClient,
        ILighterRealtimeState realtimeState,
        IOptions<SimpleGridConfig> config,
        IOptions<LighterOptions> lighterOptions,
        ILogger<DashboardStateService> logger)
    {
        _engine = engine;
        _queryClient = queryClient;
        _realtimeState = realtimeState;
        _config = config.Value;
        _lighterOptions = lighterOptions.Value;
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
        decimal currentPrice = 0;

        var wsOrderBook = _realtimeState.GetOrderBook(marketId);
        if (wsOrderBook != null && wsOrderBook.MidPrice > 0)
            currentPrice = wsOrderBook.MidPrice;
        else
        {
            var ob = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: ct);
            currentPrice = ob.LastTradePrice;
        }

        decimal equity = 0, positionSize = 0, unrealizedPnl = 0;
        var wsAccount = _realtimeState.GetAccount();
        if (wsAccount != null && _realtimeState.IsConnected)
        {
            equity = wsAccount.Collateral;
            if (wsAccount.Positions.TryGetValue(marketId, out var p))
            {
                positionSize = p.Size;
                unrealizedPnl = p.UnrealizedPnl;
            }
        }
        else
        {
            var account = await _queryClient.GetAccountAsync(_lighterOptions.AccountIndex, ct);
            decimal.TryParse(account.Collateral, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out equity);
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
