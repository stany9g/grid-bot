using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.OrderBook;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Manages the complete lifecycle of trading grids.
/// Thread-safe for concurrent access.
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
    /// </summary>
    private const decimal PriceShiftThreshold = 0.5m;

    public GridLifecycleService(
        IGridCalculator gridCalculator,
        IGridOrderManager orderManager,
        IMarketDataService marketDataService,
        IIndicatorService indicatorService,
        IOrderBookAnalyzer orderBookAnalyzer,
        IRiskConfiguration config,
        ITradingStateService stateService,
        ILogger<GridLifecycleService> logger)
    {
        ArgumentNullException.ThrowIfNull(gridCalculator);
        ArgumentNullException.ThrowIfNull(orderManager);
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(indicatorService);
        ArgumentNullException.ThrowIfNull(orderBookAnalyzer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(logger);

        _gridCalculator = gridCalculator;
        _orderManager = orderManager;
        _marketDataService = marketDataService;
        _indicatorService = indicatorService;
        _orderBookAnalyzer = orderBookAnalyzer;
        _config = config;
        _stateService = stateService;
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

            // Verify trading state allows initialization
            if (_stateService.CurrentState != TradingState.Active)
            {
                throw new InvalidOperationException(
                    $"Cannot initialize grid: trading state is {_stateService.CurrentState}");
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

            // Calculate order sizes for each level
            await UpdateOrderSizesAsync(marketId, levels, ct).ConfigureAwait(false);

            // Create grid state
            var gridState = new GridState
            {
                MarketId = marketId,
                Status = GridStatus.Active,
                Parameters = parameters,
                Levels = levels
            };

            // Place orders
            var result = await _orderManager.PlaceGridOrdersAsync(marketId, levels, ct)
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

        if (_stateService.CurrentState != TradingState.Active)
        {
            return new GridUpdateResult { Message = $"Trading state is {_stateService.CurrentState}" };
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

            // Count only NEW fills (not already filled)
            var fillsDetected = gridState.Levels.Count(l =>
                l.Status == GridLevelStatus.Filled &&
                !previouslyFilled.Contains(l.ClientOrderIndex));
            gridState.TotalFills += fillsDetected;

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
            var currentSpacing = _gridCalculator.CalculateGridSpacingFromAtr(atrPercent);
            var spacingChange = Math.Abs(currentSpacing - gridState.Parameters.GridSpacing) /
                               gridState.Parameters.GridSpacing;
            var shouldRebuild = spacingChange > AtrChangeThreshold;

            if (shouldRebuild)
            {
                // Full rebuild needed
                _logger.LogInformation(
                    "ATR changed significantly ({Change:P0}), rebuilding grid",
                    spacingChange);

                gridState.Status = GridStatus.Rebuilding;
                await _orderManager.CancelAllGridOrdersAsync(marketId, ct).ConfigureAwait(false);

                var newParams = _gridCalculator.CalculateGridParameters(currentPrice, atr, atrPercent);
                var newLevels = _gridCalculator.CalculateGridLevels(currentPrice, newParams);

                await UpdateOrderSizesAsync(marketId, newLevels, ct).ConfigureAwait(false);

                var result = await _orderManager.PlaceGridOrdersAsync(marketId, newLevels, ct)
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

                foreach (var level in filledLevels)
                {
                    level.Status = GridLevelStatus.Pending;
                    level.OrderId = null;
                    level.ClientOrderIndex = null;
                }

                if (filledLevels.Count > 0)
                {
                    await UpdateOrderSizesAsync(marketId, filledLevels, ct).ConfigureAwait(false);
                    var result = await _orderManager.PlaceGridOrdersAsync(marketId, filledLevels, ct)
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
            await UpdateOrderSizesAsync(gridState.MarketId, pendingLevels, ct).ConfigureAwait(false);
            await _orderManager.PlaceGridOrdersAsync(gridState.MarketId, pendingLevels, ct)
                .ConfigureAwait(false);
        }

        // Update state
        gridState.Parameters = newParams;
        gridState.Levels = newLevels;
        gridState.LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates order sizes for grid levels based on capital allocation.
    /// </summary>
    private async Task UpdateOrderSizesAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        CancellationToken ct)
    {
        if (levels.Count == 0)
        {
            return;
        }

        var totalLevels = levels.Count;
        var averagePrice = levels.Average(l => l.Price);

        var baseSize = await _orderManager.CalculateOrderSizeAsync(marketId, averagePrice, totalLevels, ct)
            .ConfigureAwait(false);

        foreach (var level in levels)
        {
            // Update order size based on capital allocation
            level.Size = baseSize;
        }

        _logger.LogDebug(
            "Updated order sizes for {Count} levels: {Size} per level",
            levels.Count, baseSize);
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
