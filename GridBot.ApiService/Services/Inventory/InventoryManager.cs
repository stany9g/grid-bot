using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.State;
using GridBot.Lighter;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Inventory;

/// <summary>
/// Implementation of inventory management with rebalancing calculations.
/// Thread-safe for concurrent access across multiple markets.
/// </summary>
public sealed class InventoryManager : IInventoryManager
{
    private readonly ILighterQueryClient _queryClient;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingStateService;
    private readonly IMarketDataService _marketDataService;
    private readonly ILogger<InventoryManager> _logger;
    private readonly LighterOptions _lighterOptions;

    /// <summary>
    /// Tracks rebalance amounts per market per hour.
    /// Key: marketId, Value: (HourStart, TotalRebalancedPercent)
    /// </summary>
    private readonly ConcurrentDictionary<int, (DateTimeOffset HourStart, decimal TotalRebalanced)> _hourlyRebalanceTracker = new();

    public InventoryManager(
        ILighterQueryClient queryClient,
        IRiskConfiguration riskConfig,
        ITradingStateService tradingStateService,
        IMarketDataService marketDataService,
        IOptions<LighterOptions> lighterOptions,
        ILogger<InventoryManager> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _riskConfig = riskConfig ?? throw new ArgumentNullException(nameof(riskConfig));
        _tradingStateService = tradingStateService ?? throw new ArgumentNullException(nameof(tradingStateService));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _lighterOptions = lighterOptions?.Value ?? throw new ArgumentNullException(nameof(lighterOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InventoryAnalysis> AnalyzeInventoryAsync(int marketId, CancellationToken ct = default)
    {
        var trendOptions = _riskConfig.Trend;
        var currentTrendState = _tradingStateService.CurrentTrendState;

        // Get account data from Lighter
        var account = await _queryClient.GetAccountAsync(_lighterOptions.AccountIndex, ct);

        // Get current price for crypto valuation
        var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct);

        // Calculate inventory values
        var (cryptoValueUsd, usdtBalance, totalPortfolioUsd) = CalculatePortfolioValues(account, marketId, currentPrice);

        // Calculate allocations
        var cryptoAllocation = totalPortfolioUsd > 0 ? (cryptoValueUsd / totalPortfolioUsd) * 100 : 0;
        var usdtAllocation = totalPortfolioUsd > 0 ? (usdtBalance / totalPortfolioUsd) * 100 : 0;
        var currentSkew = cryptoAllocation;

        // Get target skew based on trend
        var targetSkew = CalculateTargetSkew(currentTrendState);

        // Calculate rebalance delta
        var rebalanceDelta = CalculateRebalanceDelta(currentSkew, targetSkew);

        // Determine if rebalance is needed
        var rebalanceNeeded = ShouldRebalance(currentSkew, targetSkew, trendOptions.RebalanceTolerancePercent);
        var isEmergency = IsEmergencyRebalance(currentSkew, targetSkew);

        // Check max skew limit - halt if exceeded
        var haltRequired = currentSkew > trendOptions.MaxSkewPercent || usdtAllocation > trendOptions.MaxSkewPercent;

        // Determine rebalance direction
        var direction = rebalanceDelta > 0 ? RebalanceDirection.BuyCrypto :
                        rebalanceDelta < 0 ? RebalanceDirection.SellCrypto :
                        RebalanceDirection.None;

        // Calculate max rebalance amount considering hourly limit
        var maxRebalanceAmount = CalculateMaxRebalanceAmount(marketId, trendOptions.MaxRebalanceRatePercent);

        // Build reason string
        var reason = BuildAnalysisReason(
            currentSkew, targetSkew, rebalanceDelta, rebalanceNeeded, isEmergency, haltRequired, currentTrendState);

        _logger.LogDebug(
            "Inventory analysis for market {MarketId}: Crypto={CryptoAlloc:F1}%, USDT={UsdtAlloc:F1}%, Target={Target:F1}%, Delta={Delta:F1}%",
            marketId, cryptoAllocation, usdtAllocation, targetSkew, rebalanceDelta);

        return new InventoryAnalysis
        {
            CryptoAllocation = cryptoAllocation,
            UsdtAllocation = usdtAllocation,
            CurrentSkew = currentSkew,
            TargetSkew = targetSkew,
            RebalanceDelta = rebalanceDelta,
            RebalanceNeeded = rebalanceNeeded,
            IsEmergency = isEmergency,
            Direction = direction,
            MaxRebalanceAmount = maxRebalanceAmount,
            Reason = reason,
            TotalPortfolioValueUsd = totalPortfolioUsd,
            CryptoValueUsd = cryptoValueUsd,
            UsdtBalance = usdtBalance,
            HaltRequired = haltRequired
        };
    }

    /// <inheritdoc />
    public decimal CalculateTargetSkew(TrendState trendState)
    {
        return InventoryState.GetTargetSkewForTrend(trendState);
    }

    /// <inheritdoc />
    public decimal CalculateRebalanceDelta(decimal currentSkew, decimal targetSkew)
    {
        return targetSkew - currentSkew;
    }

    /// <inheritdoc />
    public bool ShouldRebalance(decimal currentSkew, decimal targetSkew, decimal threshold = 5m)
    {
        var delta = Math.Abs(CalculateRebalanceDelta(currentSkew, targetSkew));
        return delta > threshold;
    }

    /// <inheritdoc />
    public bool IsEmergencyRebalance(decimal currentSkew, decimal targetSkew)
    {
        var trendOptions = _riskConfig.Trend;
        var delta = Math.Abs(CalculateRebalanceDelta(currentSkew, targetSkew));
        return delta > trendOptions.EmergencyRebalanceThresholdPercent;
    }

    private (decimal CryptoValueUsd, decimal UsdtBalance, decimal TotalPortfolioUsd) CalculatePortfolioValues(
        Lighter.Models.Api.Account account,
        int marketId,
        decimal currentPrice)
    {
        // Parse collateral (USDC balance) - API returns human-readable values, no scaling needed
        var collateral = decimal.TryParse(account.Collateral, out var c) ? c : 0;

        // Find position for this market
        var position = account.Positions?.FirstOrDefault(p => p.MarketId == marketId);
        var positionSize = 0m;

        if (position != null && decimal.TryParse(position.Size, out var size))
        {
            // Position size is typically in base asset units
            positionSize = size;
        }

        // Calculate crypto value in USD
        var cryptoValueUsd = Math.Abs(positionSize) * currentPrice;

        // The "USDT" balance is the collateral minus margin used
        // For simplicity, we use collateral as the quote balance
        var usdtBalance = collateral;

        // Total portfolio = collateral (which includes unrealized PnL in Lighter)
        var totalPortfolioUsd = collateral;

        // If we have a position, its unrealized PnL is already included in collateral by Lighter
        // API returns human-readable values, no scaling needed

        // Ensure we have a valid total
        if (totalPortfolioUsd <= 0)
        {
            totalPortfolioUsd = cryptoValueUsd + usdtBalance;
        }

        return (cryptoValueUsd, usdtBalance, totalPortfolioUsd);
    }

    private decimal CalculateMaxRebalanceAmount(int marketId, decimal maxRatePercent)
    {
        var now = DateTimeOffset.UtcNow;
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        var tracker = _hourlyRebalanceTracker.GetOrAdd(marketId, _ => (currentHour, 0m));

        // Reset if new hour
        if (tracker.HourStart != currentHour)
        {
            tracker = (currentHour, 0m);
            _hourlyRebalanceTracker[marketId] = tracker;
        }

        // Remaining capacity this hour
        var remaining = maxRatePercent - tracker.TotalRebalanced;
        return Math.Max(0, remaining);
    }

    /// <summary>
    /// Records a rebalance amount for rate limiting.
    /// Called by RebalancingService after executing a rebalance.
    /// </summary>
    internal void RecordRebalanceAmount(int marketId, decimal amountPercent)
    {
        var now = DateTimeOffset.UtcNow;
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

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
    }

    private static string BuildAnalysisReason(
        decimal currentSkew,
        decimal targetSkew,
        decimal delta,
        bool rebalanceNeeded,
        bool isEmergency,
        bool haltRequired,
        TrendState trendState)
    {
        if (haltRequired)
        {
            return $"HALT: Inventory skew {currentSkew:F1}% exceeds max 90%. Trading should be paused.";
        }

        if (!rebalanceNeeded)
        {
            return $"Inventory balanced. Current={currentSkew:F1}%, Target={targetSkew:F1}% (Trend: {trendState}). Delta {delta:F1}% within tolerance.";
        }

        if (isEmergency)
        {
            return $"EMERGENCY: Delta {delta:F1}% exceeds 30%. Force rebalance from {currentSkew:F1}% to {targetSkew:F1}% (Trend: {trendState}).";
        }

        var action = delta > 0 ? "BUY crypto" : "SELL crypto";
        return $"Rebalance needed: {action} to move from {currentSkew:F1}% to {targetSkew:F1}% (Trend: {trendState}). Delta={delta:F1}%.";
    }
}
