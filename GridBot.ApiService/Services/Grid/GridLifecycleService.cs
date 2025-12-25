// ============================================================================
// DEPRECATED: This service is part of the legacy ApiService implementation.
// It will be replaced by the modular architecture in GridBot.Core, 
// GridBot.TrendIntelligence, GridBot.MoonBag, and GridBot.AdvancedRisk.
// See REFACTORING_PROGRESS.md for migration status.
// ============================================================================
using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Capacity;
using GridBot.ApiService.Services.Connectivity;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.OrderBook;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using GridBot.ApiService.Services.Validation;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Manages the complete lifecycle of trading grids.
/// Thread-safe for concurrent access.
/// NEVER HALT: Grid always runs, capacity controls behavior.
/// </summary>
public sealed class GridLifecycleService : IGridLifecycleService, IDisposable
{
    private readonly IGridCalculator _gridCalculator;
    private readonly IGridOrderManager _orderManager;
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorService _indicatorService;
    private readonly IOrderBookAnalyzer _orderBookAnalyzer;
    private readonly IRiskConfiguration _config;
    private readonly ITradingStateService _stateService;
    private readonly IOperationalCapacityService _capacityService;
    private readonly ILossMonitor _lossMonitor;
    private readonly IWebSocketHealthMonitor _wsHealthMonitor;
    private readonly IPreTradeValidator _preTradeValidator;
    private readonly ILogger<GridLifecycleService> _logger;

    private readonly ConcurrentDictionary<int, GridState> _gridStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gridLocks = new();
    private bool _disposed;

    /// <summary>
    /// ATR change threshold that triggers parameter recalculation (25%).
    /// </summary>
    private const decimal AtrChangeThreshold = 0.25m;

    /// <summary>
    /// Price movement threshold that triggers a grid shift (relative to grid width).
    /// Value of 0.10 means shift when price deviates by 10% of half-width.
    /// For a 10% grid width: shift at 0.5% price deviation.
    /// For a 4% grid width: shift at 0.2% price deviation.
    /// </summary>
    private const decimal PriceShiftThreshold = 0.10m;

    public GridLifecycleService(
        IGridCalculator gridCalculator,
        IGridOrderManager orderManager,
        IMarketDataService marketDataService,
        IIndicatorService indicatorService,
        IOrderBookAnalyzer orderBookAnalyzer,
        IRiskConfiguration config,
        ITradingStateService stateService,
        IOperationalCapacityService capacityService,
        ILossMonitor lossMonitor,
        IWebSocketHealthMonitor wsHealthMonitor,
        IPreTradeValidator preTradeValidator,
        ILogger<GridLifecycleService> logger)
    {
        ArgumentNullException.ThrowIfNull(gridCalculator);
        ArgumentNullException.ThrowIfNull(orderManager);
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(indicatorService);
        ArgumentNullException.ThrowIfNull(orderBookAnalyzer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(capacityService);
        ArgumentNullException.ThrowIfNull(lossMonitor);
        ArgumentNullException.ThrowIfNull(wsHealthMonitor);
        ArgumentNullException.ThrowIfNull(preTradeValidator);
        ArgumentNullException.ThrowIfNull(logger);

        _gridCalculator = gridCalculator;
        _orderManager = orderManager;
        _marketDataService = marketDataService;
        _indicatorService = indicatorService;
        _orderBookAnalyzer = orderBookAnalyzer;
        _config = config;
        _stateService = stateService;
        _capacityService = capacityService;
        _lossMonitor = lossMonitor;
        _wsHealthMonitor = wsHealthMonitor;
        _preTradeValidator = preTradeValidator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GridState> InitializeGridAsync(int marketId, CancellationToken ct = default)
    {
        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            _logger.LogInformation("Initializing grid for market {MarketId}", marketId);

            // CRITICAL: Cancel any existing orders on the exchange before creating new grid
            // This prevents order accumulation on app restart (orders persist on exchange but grid state is in-memory)
            var (cancellationSuccess, cancelledCount) = await _orderManager.CancelExistingOrdersOnStartupAsync(marketId, ct)
                .ConfigureAwait(false);

            if (!cancellationSuccess)
            {
                _logger.LogError(
                    "Failed to verify cancellation of existing orders for market {MarketId}. " +
                    "Grid initialization aborted to prevent order accumulation.",
                    marketId);
                throw new InvalidOperationException(
                    $"Cannot initialize grid for market {marketId}: failed to cancel existing orders. " +
                    "This prevents duplicate orders on the exchange.");
            }

            if (cancelledCount > 0)
            {
                _logger.LogInformation(
                    "Cleaned up {Count} stale orders from previous session for market {MarketId}",
                    cancelledCount, marketId);
            }

            // Verify trading state - NEVER HALT
            // In protective mode, allow reduce-only grid initialization
            var state = _stateService.CurrentState;
            var reduceOnlyMode = state == TradingState.Degraded_ProtectiveMode;

            if (reduceOnlyMode)
            {
                _logger.LogInformation(
                    "Protective mode: Initializing reduce-only grid for market {MarketId}",
                    marketId);
            }

            // Get market data
            var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct)
                .ConfigureAwait(false);

            var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1h", 24, ct)
                .ConfigureAwait(false);

            // Calculate ATR
            var atr = _indicatorService.CalculateAtr(candles);
            var atrPercent = currentPrice > 0 ? (atr / currentPrice) * 100 : 1.0m;

            // Get order book for liquidity clusters
            var orderBook = await _marketDataService.GetOrderBookSnapshotAsync(marketId, 20, ct)
                .ConfigureAwait(false);

            var averageDepth = (orderBook.TotalBidDepth + orderBook.TotalAskDepth) / 40m; // 20 levels each side
            var clusters = _orderBookAnalyzer.DetectLiquidityClusters(orderBook, averageDepth);

            // Calculate grid parameters
            var parameters = _gridCalculator.CalculateGridParameters(currentPrice, atr, atrPercent);

            // Calculate grid levels
            var levels = _gridCalculator.CalculateGridLevels(currentPrice, parameters, clusters);

            // In reduce-only mode (protective mode), filter to only sell orders
            if (reduceOnlyMode)
            {
                levels = levels.Where(l => !l.IsBid).ToList(); // Keep only asks (sells)
                _logger.LogInformation(
                    "Reduce-only mode: Filtered to {Count} sell orders for market {MarketId}",
                    levels.Count, marketId);
            }

            // Calculate order sizes for each level
            await UpdateOrderSizesAsync(marketId, levels, levels.Count, ct).ConfigureAwait(false);

            // Create grid state
            var gridState = new GridState
            {
                MarketId = marketId,
                Status = GridStatus.Active,
                Parameters = parameters,
                Levels = levels
            };

            // H.3 CRITICAL: Validate against market depth and place orders
            var result = await ValidateAndPlaceOrdersAsync(marketId, levels, ct)
                .ConfigureAwait(false);

            if (result.OrdersFailed > 0)
            {
                _logger.LogWarning(
                    "Some grid orders failed to place: {Placed}/{Total}",
                    result.OrdersPlaced, levels.Count);
            }

            // Store state
            _gridStates[marketId] = gridState;

            _logger.LogInformation(
                "Grid initialized for market {MarketId}: {Orders} orders, spacing={Spacing:F2}%, width={Width:F2}%",
                marketId, result.OrdersPlaced, parameters.GridSpacing, parameters.TotalWidth);

            return gridState;
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<GridState?> GetCurrentGridStateAsync(int marketId, CancellationToken ct = default)
    {
        _gridStates.TryGetValue(marketId, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public async Task<GridUpdateResult> UpdateGridAsync(int marketId, CancellationToken ct = default)
    {
        if (!_gridStates.TryGetValue(marketId, out var gridState))
        {
            _logger.LogWarning("Cannot update grid for market {MarketId}: not initialized", marketId);
            return GridUpdateResult.NoChanges();
        }

        if (gridState.Status == GridStatus.Paused)
        {
            return new GridUpdateResult { Message = "Grid is paused" };
        }

        // H.2 CRITICAL: Check WebSocket health before grid operations
        // Rule 1: IF websocket_disconnected THEN pause_grid_immediately
        if (_wsHealthMonitor.ShouldPauseGrid)
        {
            _logger.LogWarning(
                "Grid update blocked for market {MarketId}: WebSocket unhealthy - {Reason}",
                marketId, _wsHealthMonitor.UnhealthyReason);

            return new GridUpdateResult
            {
                Message = $"Grid paused: WebSocket unhealthy - {_wsHealthMonitor.UnhealthyReason}"
            };
        }

        // NEVER HALT: Allow updates in all states, including protective mode
        // In protective mode, we still need to monitor fills and manage position
        var currentState = _stateService.CurrentState;
        var reduceOnlyMode = currentState == TradingState.Degraded_ProtectiveMode;

        if (reduceOnlyMode)
        {
            _logger.LogDebug("Protective mode: Updating grid in reduce-only mode for market {MarketId}", marketId);
        }

        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Track existing filled orders BEFORE sync to avoid double-counting
            var previouslyFilled = gridState.Levels
                .Where(l => l.Status == GridLevelStatus.Filled)
                .Select(l => l.ClientOrderIndex)
                .ToHashSet();

            // Sync order status to detect fills
            await _orderManager.SyncOrderStatusAsync(marketId, gridState.Levels, ct)
                .ConfigureAwait(false);

            // Get newly filled levels (not already filled before sync)
            var newlyFilledLevels = gridState.Levels
                .Where(l => l.Status == GridLevelStatus.Filled &&
                            !previouslyFilled.Contains(l.ClientOrderIndex))
                .ToList();

            var fillsDetected = newlyFilledLevels.Count;
            gridState.TotalFills += fillsDetected;

            // BUG FIX: Record P&L for each fill to the risk sentinel
            // This enables the rolling window loss limit monitoring
            if (fillsDetected > 0)
            {
                await RecordFillPnlAsync(marketId, gridState, newlyFilledLevels, ct).ConfigureAwait(false);
            }

            // EC-002: Check if all orders were cancelled externally while position exists
            // Use ClientOrderIndex (set when we place orders) rather than OrderId
            var levelsWithClientOrderIndex = gridState.Levels.Count(l =>
                l.Status == GridLevelStatus.Active && l.ClientOrderIndex.HasValue);
            var inventory = _stateService.CurrentInventory;
            var hasPosition = Math.Abs(inventory.CurrentSkew) > 5; // More than 5% exposure (long or short) = has position

            // FIX: Don't trigger EC-002 if we couldn't verify order status (WebSocket data issue)
            // Check if the order sync was skipped due to WebSocket returning 0 orders
            // If we have levels with ClientOrderIndex but all were marked as filled in a single sync,
            // this is likely a WebSocket data issue, not actual fills
            var allLevelsFilled = gridState.Levels.Count(l => l.Status == GridLevelStatus.Filled);
            var possibleWebSocketIssue = levelsWithClientOrderIndex == 0 &&
                                         allLevelsFilled == gridState.Levels.Count &&
                                         fillsDetected == gridState.Levels.Count;

            if (possibleWebSocketIssue)
            {
                _logger.LogWarning(
                    "EC-002 SUPPRESSED for market {MarketId}: All {Count} orders marked as filled simultaneously. " +
                    "This is likely a WebSocket data issue, not actual external cancellation. " +
                    "Skipping grid rebuild to prevent duplicate orders.",
                    marketId, allLevelsFilled);
            }
            else if (levelsWithClientOrderIndex == 0 && hasPosition && gridState.Levels.Count > 0)
            {
                _logger.LogWarning(
                    "EC-002: All orders cancelled externally for market {MarketId}. Position exists (skew={Skew:F1}%). Rebuilding grid.",
                    marketId, inventory.CurrentSkew);

                // Reinitialize the grid to protect the position
                gridState.Status = GridStatus.Rebuilding;
            }

            // Get current market data
            var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct)
                .ConfigureAwait(false);

            var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1h", 24, ct)
                .ConfigureAwait(false);

            var atr = _indicatorService.CalculateAtr(candles);
            var atrPercent = currentPrice > 0 ? (atr / currentPrice) * 100 : 1.0m;

            var ordersAdded = 0;
            var ordersCancelled = 0;
            var gridShifted = false;
            var parametersChanged = false;

            // Check if price moved outside grid bounds
            var priceOutsideBounds = currentPrice < gridState.Parameters.LowerBound ||
                                      currentPrice > gridState.Parameters.UpperBound;

            // Check if price moved significantly from center
            var priceDeviation = Math.Abs(currentPrice - gridState.Parameters.CenterPrice) /
                                 gridState.Parameters.CenterPrice;
            var halfWidth = gridState.Parameters.TotalWidth / 200m;
            var shouldShift = priceDeviation > halfWidth * PriceShiftThreshold;

            // Check if ATR changed significantly
            // FIX: Use CalculateGridParameters for comparison to ensure consistent calculation
            // Previously used CalculateGridSpacingFromAtr which doesn't apply width constraints,
            // causing false 52% change detection immediately after initialization
            var proposedParams = _gridCalculator.CalculateGridParameters(currentPrice, atr, atrPercent);
            var currentSpacing = proposedParams.GridSpacing;

            // FIX Finding 5: Guard against division by zero
            decimal spacingChange = 0m;
            if (gridState.Parameters.GridSpacing > 0)
            {
                spacingChange = Math.Abs(currentSpacing - gridState.Parameters.GridSpacing) /
                               gridState.Parameters.GridSpacing;
            }
            else
            {
                _logger.LogWarning(
                    "Invalid grid spacing {Spacing} for market {MarketId}. Forcing rebuild.",
                    gridState.Parameters.GridSpacing, marketId);
                spacingChange = 1.0m; // Force rebuild
            }

            var shouldRebuild = spacingChange > AtrChangeThreshold;

            // EC-002 triggered rebuild or ATR-based rebuild
            if (gridState.Status == GridStatus.Rebuilding || shouldRebuild)
            {
                // Full rebuild needed
                var rebuildReason = gridState.Status == GridStatus.Rebuilding
                    ? "EC-002 external cancellation detected"
                    : $"ATR changed significantly ({spacingChange:P0})";

                _logger.LogInformation("Rebuilding grid for market {MarketId}: {Reason}", marketId, rebuildReason);

                gridState.Status = GridStatus.Rebuilding;
                await _orderManager.CancelAllGridOrdersAsync(marketId, ct).ConfigureAwait(false);

                // Reuse proposedParams calculated above instead of recalculating
                var newParams = proposedParams;
                var newLevels = _gridCalculator.CalculateGridLevels(currentPrice, newParams);

                await UpdateOrderSizesAsync(marketId, newLevels, newLevels.Count, ct).ConfigureAwait(false);

                // H.3 CRITICAL: Validate against market depth and place orders
                var result = await ValidateAndPlaceOrdersAsync(marketId, newLevels, ct)
                    .ConfigureAwait(false);

                gridState.Parameters = newParams;
                gridState.Levels = newLevels;
                gridState.Status = GridStatus.Active;
                gridState.RebuildCount++;
                parametersChanged = true;
                ordersAdded = result.OrdersPlaced;
            }
            else if (shouldShift || priceOutsideBounds)
            {
                // Grid shift needed
                await ShiftGridInternalAsync(gridState, currentPrice, ct).ConfigureAwait(false);
                gridShifted = true;
                gridState.ShiftCount++;

                // Count affected orders (approximation)
                ordersCancelled = gridState.Levels.Count(l => l.Status == GridLevelStatus.Cancelled);
                ordersAdded = gridState.Levels.Count(l => l.Status == GridLevelStatus.Active);
            }
            else
            {
                // Just replace filled orders
                var filledLevels = gridState.Levels.Where(l => l.Status == GridLevelStatus.Filled).ToList();

                // In reduce-only mode (protective mode), only replace sell orders
                if (reduceOnlyMode)
                {
                    filledLevels = filledLevels.Where(l => !l.IsBid).ToList();
                }

                if (filledLevels.Count > 0)
                {
                    // FIX Finding 1: Use thread-safe method to reset levels under order manager's lock
                    // This ensures no race condition between GridLifecycleService and GridOrderManager
                    await _orderManager.ResetFilledLevelsToPendingAsync(filledLevels, ct).ConfigureAwait(false);

                    // Use total grid level count for capital allocation, not just the filled subset
                    await UpdateOrderSizesAsync(marketId, filledLevels, gridState.Levels.Count, ct).ConfigureAwait(false);

                    // H.3 CRITICAL: Validate against market depth and place orders
                    var result = await ValidateAndPlaceOrdersAsync(marketId, filledLevels, ct)
                        .ConfigureAwait(false);
                    ordersAdded = result.OrdersPlaced;
                }
            }

            gridState.LastUpdatedAt = DateTimeOffset.UtcNow;

            return new GridUpdateResult
            {
                GridShifted = gridShifted,
                ParametersChanged = parametersChanged,
                OrdersAdded = ordersAdded,
                OrdersCancelled = ordersCancelled,
                FillsDetected = fillsDetected
            };
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ShiftGridAsync(int marketId, decimal newCenterPrice, CancellationToken ct = default)
    {
        if (!_gridStates.TryGetValue(marketId, out var gridState))
        {
            throw new InvalidOperationException($"Grid not initialized for market {marketId}");
        }

        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await ShiftGridInternalAsync(gridState, newCenterPrice, ct).ConfigureAwait(false);
            gridState.ShiftCount++;
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task PauseGridAsync(int marketId, CancellationToken ct = default)
    {
        if (!_gridStates.TryGetValue(marketId, out var gridState))
        {
            return;
        }

        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            gridState.Status = GridStatus.Paused;
            gridState.LastUpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Grid paused for market {MarketId}", marketId);
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResumeGridAsync(int marketId, CancellationToken ct = default)
    {
        if (!_gridStates.TryGetValue(marketId, out var gridState))
        {
            return;
        }

        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            gridState.Status = GridStatus.Active;
            gridState.LastUpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Grid resumed for market {MarketId}", marketId);
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task TeardownGridAsync(int marketId, CancellationToken ct = default)
    {
        var gridLock = GetGridLock(marketId);
        await gridLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await _orderManager.CancelAllGridOrdersAsync(marketId, ct).ConfigureAwait(false);
            _gridStates.TryRemove(marketId, out _);
            _logger.LogInformation("Grid torn down for market {MarketId}", marketId);
        }
        finally
        {
            gridLock.Release();
        }
    }

    /// <summary>
    /// Internal method to shift grid to a new center price.
    /// </summary>
    private async Task ShiftGridInternalAsync(
        GridState gridState,
        decimal newCenterPrice,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Shifting grid from {OldCenter} to {NewCenter}",
            gridState.Parameters.CenterPrice, newCenterPrice);

        var oldParams = gridState.Parameters;

        // Create new parameters with same spacing but new center
        var newParams = new GridParameters
        {
            CenterPrice = newCenterPrice,
            GridSpacing = oldParams.GridSpacing,
            OrdersPerSide = oldParams.OrdersPerSide,
            UpperBound = newCenterPrice * (1 + oldParams.TotalWidth / 200m),
            LowerBound = newCenterPrice * (1 - oldParams.TotalWidth / 200m),
            TotalWidth = oldParams.TotalWidth
        };

        // Calculate new levels
        var newLevels = _gridCalculator.CalculateGridLevels(newCenterPrice, newParams);

        // Determine which orders to keep (within tolerance of new levels)
        var tolerance = oldParams.GridSpacing / 100m * 0.5m; // Half spacing tolerance

        var ordersToCancel = new List<long>();

        foreach (var oldLevel in gridState.Levels)
        {
            if (oldLevel.Status != GridLevelStatus.Active || !oldLevel.OrderId.HasValue)
            {
                continue;
            }

            var hasMatchingNewLevel = newLevels.Any(nl =>
                nl.IsBid == oldLevel.IsBid &&
                Math.Abs(nl.Price - oldLevel.Price) / oldLevel.Price <= tolerance);

            if (!hasMatchingNewLevel)
            {
                ordersToCancel.Add(oldLevel.OrderId.Value);
                oldLevel.Status = GridLevelStatus.Cancelled;
            }
            else
            {
                // Mark matching new level as already active
                var matchingNew = newLevels.First(nl =>
                    nl.IsBid == oldLevel.IsBid &&
                    Math.Abs(nl.Price - oldLevel.Price) / oldLevel.Price <= tolerance);

                matchingNew.OrderId = oldLevel.OrderId;
                matchingNew.ClientOrderIndex = oldLevel.ClientOrderIndex;
                matchingNew.Status = GridLevelStatus.Active;
            }
        }

        // Cancel old orders
        if (ordersToCancel.Count > 0)
        {
            await _orderManager.CancelGridOrdersAsync(gridState.MarketId, ordersToCancel, ct)
                .ConfigureAwait(false);
        }

        // Place new orders for pending levels
        var pendingLevels = newLevels.Where(l => l.Status == GridLevelStatus.Pending).ToList();

        if (pendingLevels.Count > 0)
        {
            // Use total new grid level count for capital allocation, not just the pending subset
            await UpdateOrderSizesAsync(gridState.MarketId, pendingLevels, newLevels.Count, ct).ConfigureAwait(false);

            // H.3 CRITICAL: Validate against market depth and place orders
            await ValidateAndPlaceOrdersAsync(gridState.MarketId, pendingLevels, ct)
                .ConfigureAwait(false);
        }

        // Update state
        gridState.Parameters = newParams;
        gridState.Levels = newLevels;
        gridState.LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates order sizes for grid levels based on capital allocation.
    /// Applies skew correction if in SkewCorrection state.
    /// </summary>
    /// <param name="marketId">The market ID.</param>
    /// <param name="levels">The levels to update sizes for.</param>
    /// <param name="totalGridLevels">Total number of levels in the full grid (for capital allocation).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task UpdateOrderSizesAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        int totalGridLevels,
        CancellationToken ct)
    {
        if (levels.Count == 0)
        {
            return;
        }

        var averagePrice = levels.Average(l => l.Price);

        var baseSize = await _orderManager.CalculateOrderSizeAsync(marketId, averagePrice, totalGridLevels, ct)
            .ConfigureAwait(false);

        // Get skew correction multipliers based on current state
        var (buyMultiplier, sellMultiplier) = GetSkewCorrectionMultipliers();

        foreach (var level in levels)
        {
            // Apply skew correction multipliers
            var multiplier = level.IsBid ? buyMultiplier : sellMultiplier;
            level.Size = baseSize * multiplier;
        }

        if (buyMultiplier != 1.0m || sellMultiplier != 1.0m)
        {
            _logger.LogInformation(
                "Applied skew correction to grid for market {MarketId}: buy={BuyMult:F2}x, sell={SellMult:F2}x",
                marketId, buyMultiplier, sellMultiplier);
        }

        _logger.LogDebug(
            "Updated order sizes for {Count} levels: base={BaseSize}, buyMult={BuyMult:F2}, sellMult={SellMult:F2}",
            levels.Count, baseSize, buyMultiplier, sellMultiplier);
    }

    /// <summary>
    /// Gets skew correction multipliers based on current trading state.
    /// When in skew correction mode, biases grid toward the correction direction.
    /// </summary>
    /// <returns>Tuple of (buyMultiplier, sellMultiplier).</returns>
    private (decimal BuyMultiplier, decimal SellMultiplier) GetSkewCorrectionMultipliers()
    {
        var currentState = _stateService.CurrentState;
        var inventory = _stateService.CurrentInventory;

        // Default: no correction
        if (currentState != TradingState.Degraded_SkewCorrection)
        {
            return (1.0m, 1.0m);
        }

        // Determine correction direction based on current vs target skew
        // This logic works for both long and short positions (perpetual futures)
        var skewDelta = inventory.CurrentSkew - inventory.TargetSkew;

        if (skewDelta > 5) // Current exposure higher than target - need to reduce exposure
        {
            // Per framework spec: buy orders = reduce by 75%, sell orders = increase by 50%
            // For longs: sell more. For shorts: this means we're less short than we should be.
            _logger.LogDebug(
                "Skew correction: Reduce exposure. Current={Current:F1}%, Target={Target:F1}%",
                inventory.CurrentSkew, inventory.TargetSkew);
            return (0.25m, 1.5m);
        }
        else if (skewDelta < -5) // Current exposure lower than target - need to increase exposure
        {
            // Per framework spec: buy orders = increase by 50%, sell orders = reduce by 75%
            // For longs: buy more. For shorts: cover some position.
            _logger.LogDebug(
                "Skew correction: Increase exposure. Current={Current:F1}%, Target={Target:F1}%",
                inventory.CurrentSkew, inventory.TargetSkew);
            return (1.5m, 0.25m);
        }

        return (1.0m, 1.0m);
    }

    /// <summary>
    /// Records P&L for filled grid orders to the loss monitor.
    /// For grid trading, P&L is calculated as follows:
    /// - BID fill (buy): No immediate P&L (position entry)
    /// - ASK fill (sell): P&L = grid spacing profit minus fees
    /// </summary>
    /// <param name="marketId">The market ID.</param>
    /// <param name="gridState">Current grid state with parameters.</param>
    /// <param name="filledLevels">Newly filled grid levels.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task RecordFillPnlAsync(
        int marketId,
        GridState gridState,
        IReadOnlyList<GridLevel> filledLevels,
        CancellationToken ct)
    {
        // Get current equity for P&L percentage calculation from LossMonitor
        var lossStatus = await _lossMonitor.GetCurrentLossStatusAsync(marketId, ct).ConfigureAwait(false);
        var currentEquity = lossStatus.CurrentEquity;

        if (currentEquity <= 0)
        {
            _logger.LogWarning(
                "Cannot record fill P&L: current equity is {Equity}. Skipping P&L recording for {Count} fills.",
                currentEquity, filledLevels.Count);
            return;
        }

        // Lighter DEX maker fee (0.02%)
        const decimal MakerFeeRate = 0.0002m;

        foreach (var level in filledLevels)
        {
            // For BID fills (buys), there's no immediate realized P&L
            // The P&L will be realized when the corresponding ASK fills
            if (level.IsBid)
            {
                _logger.LogDebug(
                    "BID fill at {Price} for market {MarketId}: position entry, no immediate P&L",
                    level.Price, marketId);
                continue;
            }

            // For ASK fills (sells), calculate realized P&L
            // Grid profit = sell price - buy price (approximately grid spacing)
            // We estimate the entry price as one grid spacing below the exit price
            var gridSpacingDecimal = gridState.Parameters.GridSpacing / 100m;
            var estimatedEntryPrice = level.Price * (1 - gridSpacingDecimal);
            var exitPrice = level.Price;
            var quantity = level.Size;

            // Gross P&L from the trade
            var grossPnl = (exitPrice - estimatedEntryPrice) * quantity;

            // Fee estimate (maker fee on both entry and exit)
            var entryFee = estimatedEntryPrice * quantity * MakerFeeRate;
            var exitFee = exitPrice * quantity * MakerFeeRate;
            var totalFees = entryFee + exitFee;

            var netPnlUsd = grossPnl - totalFees;
            var pnlPercent = (netPnlUsd / currentEquity) * 100m;

            // Update grid state realized P&L
            gridState.RealizedPnl += netPnlUsd;

            // Record to loss monitor for rolling window loss limit tracking
            await _lossMonitor.RecordTradeResultAsync(marketId, pnlPercent, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Recorded fill P&L for market {MarketId}: {PnlPercent:F4}% ({PnlUsd:F2} USD) - " +
                "EstEntry: {Entry:F2}, Exit: {Exit:F2}, Qty: {Qty:F4}, Fees: {Fees:F4}",
                marketId, pnlPercent, netPnlUsd, estimatedEntryPrice, exitPrice, quantity, totalFees);
        }
    }

    /// <summary>
    /// Validates orders against market depth and PostOnly crossing, then places them.
    /// H.3 CRITICAL: Pre-Trade Depth Check + PostOnly Spread Check
    /// - If depth validation fails with RecommendedSizeUsd, adjusts order size
    /// - If PostOnly validation fails, skips the order (price would cross spread)
    /// - Fail closed: If validation throws, reject the order
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="levels">Grid levels to place orders for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Placement result with orders placed and failed counts.</returns>
    private async Task<GridPlacementResult> ValidateAndPlaceOrdersAsync(
        int marketId,
        List<GridLevel> levels,
        CancellationToken ct)
    {
        if (levels.Count == 0)
        {
            return new GridPlacementResult { OrdersPlaced = 0, OrdersFailed = 0 };
        }

        var validatedLevels = new List<GridLevel>();
        var rejectedCount = 0;
        var crossingSkipCount = 0;

        foreach (var level in levels)
        {
            // Calculate order value in USD
            var orderSizeUsd = level.Price * level.Size;

            try
            {
                // STEP 1: Validate PostOnly price won't cross spread
                var postOnlyValidation = await _preTradeValidator.ValidatePostOnlyPriceAsync(
                    marketId,
                    level.IsBid,
                    level.Price,
                    ct).ConfigureAwait(false);

                if (!postOnlyValidation.IsValid)
                {
                    // Order would cross spread - skip this order entirely
                    // Don't mark as Cancelled, just skip - the grid shift will handle it
                    _logger.LogInformation(
                        "POST-ONLY SKIP: {Side} order at {Price:F2} would cross spread (best bid/ask: {BestBid:F2}/{BestAsk:F2}). Skipping until price returns.",
                        level.IsBid ? "BUY" : "SELL",
                        level.Price,
                        postOnlyValidation.BestBid,
                        postOnlyValidation.BestAsk);

                    crossingSkipCount++;
                    continue; // Don't place, don't mark cancelled - just skip
                }

                // STEP 2: Validate depth
                var depthValidation = await _preTradeValidator.ValidateOrderAsync(
                    marketId,
                    level.IsBid,
                    orderSizeUsd,
                    ct).ConfigureAwait(false);

                if (depthValidation.IsValid)
                {
                    validatedLevels.Add(level);
                }
                else if (depthValidation.RecommendedSizeUsd.HasValue && depthValidation.RecommendedSizeUsd.Value > 0)
                {
                    // Adjust order size to recommended
                    var adjustedSize = depthValidation.RecommendedSizeUsd.Value / level.Price;

                    _logger.LogWarning(
                        "PRE-TRADE: Adjusting {Side} order at {Price:F2} from {OriginalSize:F4} to {AdjustedSize:F4} (${OriginalUsd:F0} -> ${AdjustedUsd:F0})",
                        level.IsBid ? "BUY" : "SELL",
                        level.Price,
                        level.Size,
                        adjustedSize,
                        orderSizeUsd,
                        depthValidation.RecommendedSizeUsd.Value);

                    level.Size = adjustedSize;
                    validatedLevels.Add(level);
                }
                else
                {
                    // Reject order entirely
                    _logger.LogWarning(
                        "PRE-TRADE: Rejecting {Side} order at {Price:F2} (${SizeUsd:F0}): {Reason}",
                        level.IsBid ? "BUY" : "SELL",
                        level.Price,
                        orderSizeUsd,
                        depthValidation.Reason);

                    level.Status = GridLevelStatus.Cancelled;
                    rejectedCount++;
                }
            }
            catch (Exception ex)
            {
                // Fail closed: If validation throws, reject the order
                _logger.LogError(
                    ex,
                    "PRE-TRADE: Exception validating {Side} order at {Price:F2}. Rejecting order (fail closed).",
                    level.IsBid ? "BUY" : "SELL",
                    level.Price);

                level.Status = GridLevelStatus.Cancelled;
                rejectedCount++;
            }
        }

        if (crossingSkipCount > 0)
        {
            _logger.LogInformation(
                "POST-ONLY: Skipped {SkipCount} orders that would cross spread for market {MarketId}. " +
                "Grid will shift when price stabilizes.",
                crossingSkipCount, marketId);
        }

        if (validatedLevels.Count == 0)
        {
            _logger.LogWarning(
                "PRE-TRADE: All {Count} orders rejected/skipped for market {MarketId}. No orders will be placed.",
                levels.Count, marketId);

            return new GridPlacementResult { OrdersPlaced = 0, OrdersFailed = rejectedCount };
        }

        if (rejectedCount > 0 || crossingSkipCount > 0)
        {
            _logger.LogInformation(
                "PRE-TRADE: {Validated}/{Total} orders passed validation for market {MarketId} (rejected={Rejected}, crossing={Crossing})",
                validatedLevels.Count, levels.Count, marketId, rejectedCount, crossingSkipCount);
        }

        // Place validated orders
        var result = await _orderManager.PlaceGridOrdersAsync(marketId, validatedLevels, ct)
            .ConfigureAwait(false);

        return new GridPlacementResult
        {
            OrdersPlaced = result.OrdersPlaced,
            OrdersFailed = result.OrdersFailed + rejectedCount
        };
    }

    /// <summary>
    /// Gets or creates a lock for the specified market.
    /// </summary>
    private SemaphoreSlim GetGridLock(int marketId)
    {
        return _gridLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Disposes all grid locks and clears state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var semaphore in _gridLocks.Values)
        {
            semaphore.Dispose();
        }
        _gridLocks.Clear();
        _gridStates.Clear();
    }
}

