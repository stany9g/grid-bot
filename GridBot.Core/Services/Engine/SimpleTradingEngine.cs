using GridBot.Abstractions.Trading;
using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Configuration;
using GridBot.Core.Services.Grid;
using GridBot.Core.Services.Risk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridBot.Core.Services.Engine;

/// <summary>
/// Simple trading engine - the main orchestrator.
/// Uses runtime configuration service for adaptive parameter support.
/// Uses DEX-agnostic abstractions for exchange operations.
/// </summary>
/// <remarks>
/// This is a singleton service that maintains trading state.
/// It uses IServiceScopeFactory to resolve scoped exchange client dependencies per-operation,
/// supporting dynamic network switching.
/// </remarks>
public sealed class SimpleTradingEngine : ISimpleTradingEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGridConfigurationService _configService;
    private readonly IBasicRiskMonitor _riskMonitor;
    private readonly ILogger<SimpleTradingEngine> _logger;

    private bool _isRunning;
    private bool _isInitialized;

    // Grid state is maintained by the GridManager resolved per-operation
    // We cache the last known state for quick access
    private GridState _cachedState = new();
    private readonly object _stateLock = new();

    public SimpleTradingEngine(
        IServiceScopeFactory scopeFactory,
        IGridConfigurationService configService,
        IBasicRiskMonitor riskMonitor,
        ILogger<SimpleTradingEngine> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _riskMonitor = riskMonitor ?? throw new ArgumentNullException(nameof(riskMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public GridState State
    {
        get
        {
            lock (_stateLock)
            {
                return _cachedState;
            }
        }
    }

    public bool IsRunning => _isRunning;

    public async Task RunCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            _logger.LogDebug("Engine not running, skipping cycle");
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var gridManager = scope.ServiceProvider.GetRequiredService<IGridManager>();
            var marketData = scope.ServiceProvider.GetRequiredService<IMarketDataClient>();
            var accountClient = scope.ServiceProvider.GetRequiredService<IAccountClient>();

            // 1. Get current price and account data
            var (price, equity) = await GetMarketDataAsync(marketData, accountClient, cancellationToken).ConfigureAwait(false);

            // 2. Record price for risk monitoring
            _riskMonitor.RecordPrice(price, DateTimeOffset.UtcNow);

            // 3. Get current grid state
            var state = gridManager.State;
            UpdateCachedState(state);

            // 4. Check risk status
            var riskStatus = _riskMonitor.Check(price, equity, state.TodayPnlUsdc);

            if (!riskStatus.IsSafe)
            {
                _logger.LogWarning("Risk check failed: {Reason}", riskStatus.Reason);
                await gridManager.PauseAsync(riskStatus.Reason!, cancellationToken).ConfigureAwait(false);
                UpdateCachedState(gridManager.State);
                return;
            }

            // 5. Handle paused state with cooldown
            if (state.State == TradingState.Paused)
            {
                if (state.IsCooldownExpired)
                {
                    _logger.LogInformation("Cooldown expired, resuming trading");
                    await gridManager.ResumeAsync(price, cancellationToken).ConfigureAwait(false);
                    UpdateCachedState(gridManager.State);
                }
                return;
            }

            // 6. Initialize grid if needed
            if (!_isInitialized)
            {
                await gridManager.InitializeAsync(price, cancellationToken).ConfigureAwait(false);
                _isInitialized = true;
                UpdateCachedState(gridManager.State);
                return;
            }

            // 7. Update grid
            await gridManager.UpdateGridAsync(price, cancellationToken).ConfigureAwait(false);
            UpdateCachedState(gridManager.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in trading cycle");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var config = _configService.Current;
        _logger.LogInformation("Starting trading engine for {Market}", config.Market);
        _isRunning = true;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var gridManager = scope.ServiceProvider.GetRequiredService<IGridManager>();
        var marketData = scope.ServiceProvider.GetRequiredService<IMarketDataClient>();
        var accountClient = scope.ServiceProvider.GetRequiredService<IAccountClient>();

        // Get initial price and initialize
        var (price, _) = await GetMarketDataAsync(marketData, accountClient, cancellationToken).ConfigureAwait(false);
        await gridManager.InitializeAsync(price, cancellationToken).ConfigureAwait(false);
        _isInitialized = true;
        UpdateCachedState(gridManager.State);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping trading engine");
        _isRunning = false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var gridManager = scope.ServiceProvider.GetRequiredService<IGridManager>();

        await gridManager.CancelAllOrdersAsync(cancellationToken).ConfigureAwait(false);
        UpdateCachedState(gridManager.State);
    }

    public async Task PauseAsync(string reason, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var gridManager = scope.ServiceProvider.GetRequiredService<IGridManager>();

        await gridManager.PauseAsync(reason, cancellationToken).ConfigureAwait(false);
        UpdateCachedState(gridManager.State);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var gridManager = scope.ServiceProvider.GetRequiredService<IGridManager>();
        var marketData = scope.ServiceProvider.GetRequiredService<IMarketDataClient>();
        var accountClient = scope.ServiceProvider.GetRequiredService<IAccountClient>();

        var (price, _) = await GetMarketDataAsync(marketData, accountClient, cancellationToken).ConfigureAwait(false);
        await gridManager.ResumeAsync(price, cancellationToken).ConfigureAwait(false);
        UpdateCachedState(gridManager.State);
    }

    private async Task<(decimal Price, decimal Equity)> GetMarketDataAsync(
        IMarketDataClient marketData,
        IAccountClient accountClient,
        CancellationToken cancellationToken)
    {
        var config = _configService.Current;

        // Get current price from market data client
        var price = await marketData.GetCurrentPriceAsync(config.Market, cancellationToken).ConfigureAwait(false);

        // Get account for equity (portfolio value)
        var account = await accountClient.GetAccountAsync(cancellationToken).ConfigureAwait(false);
        var equity = account.PortfolioValue;

        return (price, equity);
    }

    private void UpdateCachedState(GridState state)
    {
        lock (_stateLock)
        {
            _cachedState = state;
        }
    }
}
