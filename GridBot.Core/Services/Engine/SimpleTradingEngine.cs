using GridBot.Abstractions.Trading;
using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Configuration;
using GridBot.Core.Services.Grid;
using GridBot.Core.Services.Risk;
using Microsoft.Extensions.Logging;

namespace GridBot.Core.Services.Engine;

/// <summary>
/// Simple trading engine - the main orchestrator.
/// Uses runtime configuration service for adaptive parameter support.
/// Uses DEX-agnostic abstractions for exchange operations.
/// </summary>
public sealed class SimpleTradingEngine : ISimpleTradingEngine
{
    private readonly IGridConfigurationService _configService;
    private readonly IGridManager _gridManager;
    private readonly IBasicRiskMonitor _riskMonitor;
    private readonly IMarketDataClient _marketData;
    private readonly IAccountClient _accountClient;
    private readonly ILogger<SimpleTradingEngine> _logger;

    private bool _isRunning;
    private bool _isInitialized;

    public SimpleTradingEngine(
        IGridConfigurationService configService,
        IGridManager gridManager,
        IBasicRiskMonitor riskMonitor,
        IMarketDataClient marketData,
        IAccountClient accountClient,
        ILogger<SimpleTradingEngine> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _gridManager = gridManager ?? throw new ArgumentNullException(nameof(gridManager));
        _riskMonitor = riskMonitor ?? throw new ArgumentNullException(nameof(riskMonitor));
        _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        _accountClient = accountClient ?? throw new ArgumentNullException(nameof(accountClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public GridState State => _gridManager.State;
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
            // 1. Get current price and account data
            var (price, equity) = await GetMarketDataAsync(cancellationToken).ConfigureAwait(false);

            // 2. Record price for risk monitoring
            _riskMonitor.RecordPrice(price, DateTimeOffset.UtcNow);

            // 3. Check risk status
            var riskStatus = _riskMonitor.Check(price, equity, State.TodayPnlUsdc);

            if (!riskStatus.IsSafe)
            {
                _logger.LogWarning("Risk check failed: {Reason}", riskStatus.Reason);
                await _gridManager.PauseAsync(riskStatus.Reason!, cancellationToken).ConfigureAwait(false);
                return;
            }

            // 4. Handle paused state with cooldown
            if (State.State == TradingState.Paused)
            {
                if (State.IsCooldownExpired)
                {
                    _logger.LogInformation("Cooldown expired, resuming trading");
                    await _gridManager.ResumeAsync(price, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            // 5. Initialize grid if needed
            if (!_isInitialized)
            {
                await _gridManager.InitializeAsync(price, cancellationToken).ConfigureAwait(false);
                _isInitialized = true;
                return;
            }

            // 6. Update grid
            await _gridManager.UpdateGridAsync(price, cancellationToken).ConfigureAwait(false);
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

        // Get initial price and initialize
        var (price, _) = await GetMarketDataAsync(cancellationToken).ConfigureAwait(false);
        await _gridManager.InitializeAsync(price, cancellationToken).ConfigureAwait(false);
        _isInitialized = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping trading engine");
        _isRunning = false;

        await _gridManager.CancelAllOrdersAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gridManager.PauseAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        var (price, _) = await GetMarketDataAsync(cancellationToken).ConfigureAwait(false);
        await _gridManager.ResumeAsync(price, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(decimal Price, decimal Equity)> GetMarketDataAsync(CancellationToken cancellationToken)
    {
        var config = _configService.Current;

        // Get current price from market data client
        var price = await _marketData.GetCurrentPriceAsync(config.Market, cancellationToken).ConfigureAwait(false);

        // Get account for equity (portfolio value)
        var account = await _accountClient.GetAccountAsync(cancellationToken).ConfigureAwait(false);
        var equity = account.PortfolioValue;

        return (price, equity);
    }
}
