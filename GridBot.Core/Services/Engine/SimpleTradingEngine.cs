using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Configuration;
using GridBot.Core.Services.Grid;
using GridBot.Core.Services.Risk;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Core.Services.Engine;

/// <summary>
/// Simple trading engine - the main orchestrator.
/// Uses runtime configuration service for adaptive parameter support.
/// </summary>
public sealed class SimpleTradingEngine : ISimpleTradingEngine
{
    private readonly IGridConfigurationService _configService;
    private readonly LighterOptions _lighterOptions;
    private readonly IGridManager _gridManager;
    private readonly IBasicRiskMonitor _riskMonitor;
    private readonly ILighterQueryClient _queryClient;
    private readonly ILogger<SimpleTradingEngine> _logger;

    private bool _isRunning;
    private bool _isInitialized;

    public SimpleTradingEngine(
        IGridConfigurationService configService,
        IOptions<LighterOptions> lighterOptions,
        IGridManager gridManager,
        IBasicRiskMonitor riskMonitor,
        ILighterQueryClient queryClient,
        ILogger<SimpleTradingEngine> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _lighterOptions = lighterOptions?.Value ?? throw new ArgumentNullException(nameof(lighterOptions));
        _gridManager = gridManager ?? throw new ArgumentNullException(nameof(gridManager));
        _riskMonitor = riskMonitor ?? throw new ArgumentNullException(nameof(riskMonitor));
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
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

        // Get order book for current price
        var orderBook = await _queryClient.GetOrderBookDetailsAsync(config.MarketIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
        var price = orderBook.LastTradePrice;

        // Get account for equity
        var account = await _queryClient.GetAccountAsync(_lighterOptions.AccountIndex, cancellationToken).ConfigureAwait(false);
        var equity = decimal.TryParse(account.TotalAssetValue, out var val) ? val : 0;

        return (price, equity);
    }
}