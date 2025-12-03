using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Inventory;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.State;
using GridBot.Lighter;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Rebalancing;

/// <summary>
/// Implementation of portfolio rebalancing with rate limiting and emergency handling.
/// Thread-safe for concurrent access across multiple markets.
/// </summary>
public sealed class RebalancingService : IRebalancingService, IDisposable
{
    private readonly ILighterCommandClient _commandClient;
    private readonly ILighterQueryClient _queryClient;
    private readonly IMarketDataService _marketDataService;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingStateService;
    private readonly InventoryManager _inventoryManager;
    private readonly ILogger<RebalancingService> _logger;
    private readonly LighterOptions _lighterOptions;
    private bool _disposed;

    /// <summary>
    /// Tracks last rebalance time per market for rate limiting.
    /// </summary>
    private readonly ConcurrentDictionary<int, DateTimeOffset> _lastRebalanceTime = new();

    /// <summary>
    /// Tracks hourly rebalance amounts per market.
    /// Key: marketId, Value: (HourStart, TotalRebalancedPercent)
    /// </summary>
    private readonly ConcurrentDictionary<int, (DateTimeOffset HourStart, decimal TotalRebalanced)> _hourlyRebalanceTracker = new();

    /// <summary>
    /// Lock for rebalance operations per market.
    /// </summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _rebalanceLocks = new();

    /// <summary>
    /// Minimum time between rebalance operations (prevents rapid-fire orders).
    /// </summary>
    private static readonly TimeSpan MinRebalanceInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Client order index counter for unique order IDs.
    /// </summary>
    private long _clientOrderCounter = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public RebalancingService(
        ILighterCommandClient commandClient,
        ILighterQueryClient queryClient,
        IMarketDataService marketDataService,
        IRiskConfiguration riskConfig,
        ITradingStateService tradingStateService,
        InventoryManager inventoryManager,
        IOptions<LighterOptions> lighterOptions,
        ILogger<RebalancingService> logger)
    {
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _riskConfig = riskConfig ?? throw new ArgumentNullException(nameof(riskConfig));
        _tradingStateService = tradingStateService ?? throw new ArgumentNullException(nameof(tradingStateService));
        _inventoryManager = inventoryManager ?? throw new ArgumentNullException(nameof(inventoryManager));
        _lighterOptions = lighterOptions?.Value ?? throw new ArgumentNullException(nameof(lighterOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RebalanceResult> ExecuteRebalanceAsync(int marketId, InventoryAnalysis analysis, CancellationToken ct = default)
    {
        // Check if we can rebalance
        if (!CanRebalanceNow(marketId))
        {
            return RebalanceResult.Failed("Rebalancing not allowed - either trading is not active or rate limit exceeded");
        }

        if (!analysis.RebalanceNeeded)
        {
            return RebalanceResult.NotNeeded(analysis.CurrentSkew);
        }

        var rebalanceLock = _rebalanceLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));

        if (!await rebalanceLock.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            return RebalanceResult.Failed("Could not acquire rebalance lock - another operation in progress");
        }

        try
        {
            return await ExecuteRebalanceInternalAsync(marketId, analysis, ct);
        }
        finally
        {
            rebalanceLock.Release();
        }
    }

    private async Task<RebalanceResult> ExecuteRebalanceInternalAsync(int marketId, InventoryAnalysis analysis, CancellationToken ct)
    {
        var trendOptions = _riskConfig.Trend;

        // Calculate the actual rebalance amount
        decimal targetDelta;
        if (analysis.IsEmergency)
        {
            // Emergency: force rebalance to within 15% of target, clamped to valid range
            var emergencyTarget = Math.Clamp(
                analysis.TargetSkew + (analysis.RebalanceDelta > 0 ? -15m : 15m),
                10m,  // Never go below 10% crypto allocation
                90m   // Never exceed 90% crypto allocation
            );
            targetDelta = emergencyTarget - analysis.CurrentSkew;

            _logger.LogWarning(
                "Emergency rebalance on market {MarketId}: Moving from {Current:F1}% to {Emergency:F1}% (target {Target:F1}%)",
                marketId, analysis.CurrentSkew, emergencyTarget, analysis.TargetSkew);
        }
        else
        {
            // Normal: respect hourly limit
            var availableCapacity = await GetAvailableRebalanceCapacityAsync(marketId, ct);
            targetDelta = Math.Sign(analysis.RebalanceDelta) * Math.Min(Math.Abs(analysis.RebalanceDelta), availableCapacity);

            if (Math.Abs(targetDelta) < 1m)
            {
                return RebalanceResult.Failed($"Hourly rebalance capacity exhausted. Available: {availableCapacity:F1}%");
            }
        }

        // Get current price for order placement
        var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct);

        // Calculate order size based on portfolio percentage
        var portfolioValue = analysis.TotalPortfolioValueUsd;
        var rebalanceValueUsd = portfolioValue * Math.Abs(targetDelta) / 100m;
        var cryptoAmount = rebalanceValueUsd / currentPrice;

        // Determine order direction
        var isAsk = analysis.Direction == RebalanceDirection.SellCrypto;

        _logger.LogInformation(
            "Executing rebalance on market {MarketId}: {Direction} {Amount:F6} crypto at ~{Price:F2} (${Value:F2}, {Percent:F1}% of portfolio)",
            marketId, isAsk ? "SELL" : "BUY", cryptoAmount, currentPrice, rebalanceValueUsd, Math.Abs(targetDelta));

        try
        {
            // Create market order for immediate execution
            var orderRequest = new CreateOrderRequest
            {
                MarketIndex = marketId,
                ClientOrderIndex = Interlocked.Increment(ref _clientOrderCounter),
                BaseAmount = ConvertToScaledAmount(cryptoAmount, marketId),
                Price = ConvertToScaledPrice(currentPrice, marketId),
                IsAsk = isAsk,
                OrderType = OrderType.Market,
                TimeInForce = TimeInForce.ImmediateOrCancel,
                OrderExpiry = OrderConstants.DefaultIocExpiry
            };

            var response = await _commandClient.CreateOrderAsync(orderRequest, priceProtection: true, ct);

            // Record the rebalance
            RecordRebalance(marketId, Math.Abs(targetDelta));

            // Calculate new skew
            var newSkew = analysis.CurrentSkew + targetDelta;

            _logger.LogInformation(
                "Rebalance completed on market {MarketId}: TxHash={TxHash}. New skew: {NewSkew:F1}%",
                marketId, response.TxHash, newSkew);

            return RebalanceResult.Succeeded(
                amountRebalanced: Math.Abs(targetDelta),
                newCryptoSkew: newSkew,
                cryptoAmount: isAsk ? -cryptoAmount : cryptoAmount,
                executionPrice: currentPrice,
                transactionHash: response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogError(ex, "Lighter API error during rebalance on market {MarketId}: {Message}", marketId, ex.Message);
            return RebalanceResult.Failed($"API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during rebalance on market {MarketId}", marketId);
            return RebalanceResult.Failed($"Unexpected error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<decimal> GetAvailableRebalanceCapacityAsync(int marketId, CancellationToken ct = default)
    {
        var trendOptions = _riskConfig.Trend;
        var now = DateTimeOffset.UtcNow;
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        var tracker = _hourlyRebalanceTracker.GetOrAdd(marketId, _ => (currentHour, 0m));

        // Reset if new hour
        if (tracker.HourStart != currentHour)
        {
            tracker = (currentHour, 0m);
            _hourlyRebalanceTracker[marketId] = tracker;
        }

        var remaining = trendOptions.MaxRebalanceRatePercent - tracker.TotalRebalanced;
        return Task.FromResult(Math.Max(0, remaining));
    }

    /// <inheritdoc />
    public bool CanRebalanceNow(int marketId)
    {
        // Check trading state
        if (_tradingStateService.CurrentState != TradingState.Active)
        {
            return false;
        }

        // Check minimum interval
        if (_lastRebalanceTime.TryGetValue(marketId, out var lastTime))
        {
            if (DateTimeOffset.UtcNow - lastTime < MinRebalanceInterval)
            {
                return false;
            }
        }

        return true;
    }

    private void RecordRebalance(int marketId, decimal amountPercent)
    {
        var now = DateTimeOffset.UtcNow;
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        // Update hourly tracker
        _hourlyRebalanceTracker.AddOrUpdate(
            marketId,
            (currentHour, amountPercent),
            (_, existing) =>
            {
                if (existing.HourStart != currentHour)
                {
                    return (currentHour, amountPercent);
                }
                return (existing.HourStart, existing.TotalRebalanced + amountPercent);
            });

        // Update last rebalance time
        _lastRebalanceTime[marketId] = now;

        // Notify inventory manager
        _inventoryManager.RecordRebalanceAmount(marketId, amountPercent);
    }

    /// <summary>
    /// Converts a decimal crypto amount to the scaled value for the API.
    /// </summary>
    private static long ConvertToScaledAmount(decimal amount, int marketId)
    {
        // Standard 8 decimal places for BTC-like assets
        // This should be market-specific in production
        const decimal baseScale = 100_000_000m;
        return (long)(amount * baseScale);
    }

    /// <summary>
    /// Converts a decimal price to the scaled value for the API.
    /// </summary>
    private static long ConvertToScaledPrice(decimal price, int marketId)
    {
        // USDC scaling (6 decimal places)
        return (long)(price * OrderConstants.UsdcTickerScale);
    }

    /// <summary>
    /// Disposes all rebalance locks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var semaphore in _rebalanceLocks.Values)
        {
            semaphore.Dispose();
        }
        _rebalanceLocks.Clear();
    }
}
