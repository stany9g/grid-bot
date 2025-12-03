using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Inventory;
using GridBot.ApiService.Services.Rebalancing;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Trend;

/// <summary>
/// Orchestrator for trend intelligence processing cycle.
/// Coordinates trend detection, inventory analysis, and rebalancing.
/// </summary>
public sealed class TrendIntelligenceService : ITrendIntelligenceService
{
    private readonly ITrendDetector _trendDetector;
    private readonly IInventoryManager _inventoryManager;
    private readonly IRebalancingService _rebalancingService;
    private readonly ITradingStateService _tradingStateService;
    private readonly ILogger<TrendIntelligenceService> _logger;

    public TrendIntelligenceService(
        ITrendDetector trendDetector,
        IInventoryManager inventoryManager,
        IRebalancingService rebalancingService,
        ITradingStateService tradingStateService,
        ILogger<TrendIntelligenceService> logger)
    {
        _trendDetector = trendDetector ?? throw new ArgumentNullException(nameof(trendDetector));
        _inventoryManager = inventoryManager ?? throw new ArgumentNullException(nameof(inventoryManager));
        _rebalancingService = rebalancingService ?? throw new ArgumentNullException(nameof(rebalancingService));
        _tradingStateService = tradingStateService ?? throw new ArgumentNullException(nameof(tradingStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TrendIntelligenceResult> ProcessTrendCycleAsync(int marketId, CancellationToken ct = default)
    {
        _logger.LogDebug("Starting trend intelligence cycle for market {MarketId}", marketId);

        TrendAnalysis? trendAnalysis = null;
        InventoryAnalysis? inventoryAnalysis = null;
        RebalanceResult? rebalanceResult = null;
        var trendStateChanged = false;
        var inventoryAdjusted = false;

        try
        {
            // Step 1: Analyze trend
            trendAnalysis = await _trendDetector.AnalyzeTrendAsync(marketId, ct);

            _logger.LogDebug(
                "Trend analysis for market {MarketId}: Current={CurrentState}, Proposed={ProposedState}, ConfirmationRequired={ConfirmRequired}",
                marketId, trendAnalysis.CurrentState, trendAnalysis.ProposedState, trendAnalysis.ConfirmationRequired);

            // Step 2: Update trend state if changed and confirmed
            var previousTrendState = _tradingStateService.CurrentTrendState;

            if (trendAnalysis.CurrentState != previousTrendState && !trendAnalysis.InCooldown)
            {
                await _tradingStateService.UpdateTrendStateAsync(trendAnalysis.CurrentState);
                trendStateChanged = true;

                _logger.LogInformation(
                    "Trend state changed on market {MarketId}: {OldState} -> {NewState}. Reason: {Reason}",
                    marketId, previousTrendState, trendAnalysis.CurrentState, trendAnalysis.Reason);
            }

            // Step 3: Analyze inventory
            inventoryAnalysis = await _inventoryManager.AnalyzeInventoryAsync(marketId, ct);

            _logger.LogDebug(
                "Inventory analysis for market {MarketId}: Current={CurrentSkew:F1}%, Target={TargetSkew:F1}%, RebalanceNeeded={RebalanceNeeded}",
                marketId, inventoryAnalysis.CurrentSkew, inventoryAnalysis.TargetSkew, inventoryAnalysis.RebalanceNeeded);

            // Step 4: Handle halt condition if needed
            if (inventoryAnalysis.HaltRequired)
            {
                _logger.LogWarning(
                    "Inventory skew exceeded max limit on market {MarketId}. Current={CurrentSkew:F1}%. Requesting halt.",
                    marketId, inventoryAnalysis.CurrentSkew);

                await _tradingStateService.TransitionToAsync(TradingState.Paused, "Max inventory skew exceeded");

                return TrendIntelligenceResult.Failed(
                    "Trading halted due to max inventory skew",
                    trendAnalysis,
                    inventoryAnalysis);
            }

            // Step 5: Execute rebalance if needed and trading is active
            if (inventoryAnalysis.RebalanceNeeded && _tradingStateService.CurrentState == TradingState.Active)
            {
                if (_rebalancingService.CanRebalanceNow(marketId))
                {
                    rebalanceResult = await _rebalancingService.ExecuteRebalanceAsync(marketId, inventoryAnalysis, ct);
                    inventoryAdjusted = rebalanceResult.Success && rebalanceResult.AmountRebalanced > 0;

                    if (rebalanceResult.Success)
                    {
                        _logger.LogInformation(
                            "Rebalance completed on market {MarketId}: Adjusted {Amount:F1}%, New skew={NewSkew:F1}%",
                            marketId, rebalanceResult.AmountRebalanced, rebalanceResult.NewCryptoSkew);

                        // Update inventory state in trading state service
                        await UpdateInventoryStateAsync(inventoryAnalysis, rebalanceResult);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Rebalance failed on market {MarketId}: {ErrorMessage}",
                            marketId, rebalanceResult.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Rebalance skipped on market {MarketId}: Cannot rebalance now (rate limit or state)",
                        marketId);
                }
            }

            // Step 6: Update inventory state even if no rebalance occurred
            if (!inventoryAdjusted)
            {
                await UpdateInventoryStateAsync(inventoryAnalysis, rebalanceResult);
            }

            return TrendIntelligenceResult.Succeeded(
                trendAnalysis,
                inventoryAnalysis,
                rebalanceResult,
                trendStateChanged,
                inventoryAdjusted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Trend intelligence cycle cancelled for market {MarketId}", marketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during trend intelligence cycle for market {MarketId}", marketId);

            return TrendIntelligenceResult.Failed(
                $"Error: {ex.Message}",
                trendAnalysis,
                inventoryAnalysis);
        }
    }

    private async Task UpdateInventoryStateAsync(InventoryAnalysis analysis, RebalanceResult? rebalanceResult)
    {
        var currentInventory = _tradingStateService.CurrentInventory;

        var newSkew = rebalanceResult is { Success: true }
            ? rebalanceResult.NewCryptoSkew
            : analysis.CurrentSkew;

        var updatedInventory = new InventoryState
        {
            CryptoAllocation = newSkew,
            UsdtAllocation = 100m - newSkew,
            TargetSkew = analysis.TargetSkew,
            CurrentSkew = newSkew,
            RebalanceNeeded = analysis.RebalanceNeeded && !(rebalanceResult?.Success ?? false),
            RebalanceDelta = analysis.TargetSkew - newSkew,
            TrendState = _tradingStateService.CurrentTrendState,
            LastTrendChange = currentInventory.LastTrendChange
        };

        updatedInventory.CalculateRebalanceNeeded();

        await _tradingStateService.UpdateInventoryAsync(updatedInventory);
    }
}
