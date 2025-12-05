using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Inventory;
using GridBot.ApiService.Services.Rebalancing;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Trend;

/// <summary>
/// Orchestrator for trend intelligence processing cycle.
/// Coordinates trend detection, inventory analysis, and rebalancing.
/// NEVER HALT: This service always continues processing, adjusting state as needed.
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
                "Inventory analysis for market {MarketId}: Current={CurrentSkew:F1}%, Target={TargetSkew:F1}%, RebalanceNeeded={RebalanceNeeded}, Bootstrap={Bootstrap}, SkewCorrection={SkewCorrection}",
                marketId, inventoryAnalysis.CurrentSkew, inventoryAnalysis.TargetSkew,
                inventoryAnalysis.RebalanceNeeded, inventoryAnalysis.IsBootstrapMode, inventoryAnalysis.SkewCorrectionMode);

            // Step 4: Handle operational state transitions based on inventory analysis
            // NEVER HALT - just transition to appropriate degraded state
            await HandleStateTransitionsAsync(marketId, inventoryAnalysis);

            // Step 5: Execute rebalance if needed
            // Rebalancing is allowed in all states except Degraded_ProtectiveMode
            var currentState = _tradingStateService.CurrentState;
            var canRebalance = currentState != TradingState.Degraded_ProtectiveMode;

            if (inventoryAnalysis.RebalanceNeeded && canRebalance)
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
                        "Rebalance skipped on market {MarketId}: Cannot rebalance now (rate limit)",
                        marketId);
                }
            }

            // Step 6: Update inventory state even if no rebalance occurred
            if (!inventoryAdjusted)
            {
                await UpdateInventoryStateAsync(inventoryAnalysis, rebalanceResult);
            }

            // Always return success - the cycle completed
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

            // Even on error, return a result - don't halt
            return TrendIntelligenceResult.Failed(
                $"Error: {ex.Message}",
                trendAnalysis,
                inventoryAnalysis);
        }
    }

    /// <summary>
    /// Handles state transitions based on inventory analysis.
    /// NEVER HALT - transitions to appropriate degraded states instead.
    /// </summary>
    private async Task HandleStateTransitionsAsync(int marketId, InventoryAnalysis inventoryAnalysis)
    {
        var currentState = _tradingStateService.CurrentState;

        // Handle bootstrap mode (no position)
        if (inventoryAnalysis.IsBootstrapMode)
        {
            if (currentState != TradingState.Degraded_Bootstrap)
            {
                _logger.LogInformation(
                    "Bootstrap mode detected on market {MarketId}. Position=0%, enabling buy-only grid.",
                    marketId);

                await _tradingStateService.TransitionToAsync(
                    TradingState.Degraded_Bootstrap,
                    "Building initial position");
            }
            return;
        }

        // Handle skew correction mode
        if (inventoryAnalysis.SkewCorrectionMode)
        {
            var direction = inventoryAnalysis.CorrectionDirection == SkewCorrectionDirection.NeedMoreCrypto
                ? "buying" : "selling";

            if (currentState != TradingState.Degraded_SkewCorrection)
            {
                _logger.LogInformation(
                    "Skew correction mode on market {MarketId}. Current={CurrentSkew:F1}%, Target range [{Min:F1}%-{Max:F1}%] requires more {Direction}.",
                    marketId, inventoryAnalysis.CurrentSkew,
                    inventoryAnalysis.AcceptableSkewMin, inventoryAnalysis.AcceptableSkewMax, direction);

                await _tradingStateService.TransitionToAsync(
                    TradingState.Degraded_SkewCorrection,
                    $"Correcting skew via {direction}");
            }
            return;
        }

        // Return to Active if correction no longer needed
        if (currentState == TradingState.Degraded_SkewCorrection ||
            currentState == TradingState.Degraded_Bootstrap)
        {
            _logger.LogInformation(
                "Returning to Active state on market {MarketId}. Skew={CurrentSkew:F1}% within acceptable range [{Min:F1}%-{Max:F1}%].",
                marketId, inventoryAnalysis.CurrentSkew,
                inventoryAnalysis.AcceptableSkewMin, inventoryAnalysis.AcceptableSkewMax);

            await _tradingStateService.TransitionToAsync(
                TradingState.Active,
                "Skew within acceptable range");
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
