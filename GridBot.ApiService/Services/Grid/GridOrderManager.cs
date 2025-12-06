using System.Globalization;
using System.Text.Json;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.MoonBag;
using GridBot.Lighter;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Manages grid orders on the Lighter DEX.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class GridOrderManager : IGridOrderManager, IDisposable
{
    private readonly ILighterCommandClient _commandClient;
    private readonly ILighterQueryClient _queryClient;
    private readonly IMoonBagManager _moonBagManager;
    private readonly IMarketScalingService _scalingService;
    private readonly IRiskConfiguration _config;
    private readonly ILogger<GridOrderManager> _logger;
    private readonly SemaphoreSlim _orderLock = new(1, 1);
    private static long _orderSequence;
    private bool _disposed;

    /// <summary>
    /// Account index for trading operations.
    /// In a production system, this would come from configuration.
    /// </summary>
    private long AccountIndex = 0;

    public GridOrderManager(
        ILighterCommandClient commandClient,
        ILighterQueryClient queryClient,
        IMoonBagManager moonBagManager,
        IMarketScalingService scalingService,
        IRiskConfiguration config,
        ILogger<GridOrderManager> logger,
        IOptions<LighterOptions> lighterOptions)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(moonBagManager);
        ArgumentNullException.ThrowIfNull(scalingService);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(lighterOptions);

        _commandClient = commandClient;
        _queryClient = queryClient;
        _moonBagManager = moonBagManager;
        _scalingService = scalingService;
        _config = config;
        _logger = logger;
        AccountIndex = lighterOptions.Value.AccountIndex;
    }

    /// <inheritdoc />
    public async Task<GridPlacementResult> PlaceGridOrdersAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            return new GridPlacementResult
            {
                OrdersPlaced = 0,
                OrdersFailed = 0
            };
        }

        var ordersPlaced = 0;
        var ordersFailed = 0;
        var errors = new List<GridOrderError>();

        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var level in levels)
            {
                if (level.Status == GridLevelStatus.Active || level.Status == GridLevelStatus.Filled)
                {
                    continue; // Skip already placed or filled orders
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    // HIGH-005 FIX: Check moon bag protection before placing sell orders
                    if (!level.IsBid) // This is a sell order (ask)
                    {
                        var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);
                        var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(
                            marketId, level.Size, currentPosition, ct).ConfigureAwait(false);

                        if (shouldBlock)
                        {
                            _logger.LogWarning(
                                "Sell order blocked by moon bag protection for market {MarketId} at level {Level}: size {Size:F4}",
                                marketId, level.LevelIndex, level.Size);

                            errors.Add(new GridOrderError
                            {
                                LevelIndex = level.LevelIndex,
                                Price = level.Price,
                                IsBid = level.IsBid,
                                ErrorMessage = "Blocked by moon bag protection"
                            });
                            ordersFailed++;
                            continue;
                        }
                    }

                    var clientOrderIndex = GenerateClientOrderIndex(level);
                    var scaledPrice = await _scalingService.ScalePriceAsync(level.Price, marketId, ct)
                        .ConfigureAwait(false);
                    var scaledSize = await _scalingService.ScaleBaseAmountAsync(level.Size, marketId, ct)
                        .ConfigureAwait(false);

                    var request = new CreateOrderRequest
                    {
                        MarketIndex = marketId,
                        ClientOrderIndex = clientOrderIndex,
                        Price = scaledPrice,
                        BaseAmount = scaledSize,
                        IsAsk = !level.IsBid, // IsAsk is true for sells
                        OrderType = OrderType.Limit,
                        TimeInForce = TimeInForce.PostOnly,
                        ReduceOnly = false,
                        OrderExpiry = OrderConstants.Default28DayOrderExpiry
                    };

                    var validationError = request.Validate();
                    if (validationError != null)
                    {
                        _logger.LogWarning(
                            "Order validation failed for level {LevelIndex}: {Error}",
                            level.LevelIndex, validationError);

                        errors.Add(new GridOrderError
                        {
                            LevelIndex = level.LevelIndex,
                            Price = level.Price,
                            IsBid = level.IsBid,
                            ErrorMessage = validationError
                        });
                        ordersFailed++;
                        continue;
                    }
                    var response = await _commandClient.CreateOrderAsync(request, priceProtection: false, ct)
                        .ConfigureAwait(false);

                    if (response.Code == 0 || response.Code == 200)
                    {
                        // Note: Order ID comes from transaction confirmation, not immediate response
                        // We store the client order index and sync status later
                        level.ClientOrderIndex = clientOrderIndex;
                        level.Status = GridLevelStatus.Active;
                        level.LastUpdatedAt = DateTimeOffset.UtcNow;
                        ordersPlaced++;

                        _logger.LogDebug(
                            "Placed {Side} order at {Price} (level {Index}), TxHash: {TxHash}",
                            level.IsBid ? "bid" : "ask",
                            level.Price,
                            level.LevelIndex,
                            response.TxHash);
                    }
                    else
                    {
                        errors.Add(new GridOrderError
                        {
                            LevelIndex = level.LevelIndex,
                            Price = level.Price,
                            IsBid = level.IsBid,
                            ErrorMessage = response.Message ?? $"API error code: {response.Code}"
                        });
                        ordersFailed++;

                        _logger.LogWarning(
                            "Failed to place {Side} order at {Price}: {Message}",
                            level.IsBid ? "bid" : "ask",
                            level.Price,
                            response.Message);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(new GridOrderError
                    {
                        LevelIndex = level.LevelIndex,
                        Price = level.Price,
                        IsBid = level.IsBid,
                        ErrorMessage = ex.Message
                    });
                    ordersFailed++;

                    _logger.LogError(ex,
                        "Exception placing {Side} order at {Price}",
                        level.IsBid ? "bid" : "ask",
                        level.Price);
                }
            }
        }
        finally
        {
            _orderLock.Release();
        }

        _logger.LogInformation(
            "Grid order placement complete: {Placed} placed, {Failed} failed",
            ordersPlaced, ordersFailed);

        return new GridPlacementResult
        {
            OrdersPlaced = ordersPlaced,
            OrdersFailed = ordersFailed,
            Errors = errors
        };
    }

    /// <inheritdoc />
    public async Task<int> CancelGridOrdersAsync(
        int marketId,
        IReadOnlyList<long> orderIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        if (orderIds.Count == 0)
        {
            return 0;
        }

        var cancelledCount = 0;

        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var orderId in orderIds)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var response = await _commandClient.CancelOrderAsync(marketId, orderId, ct)
                        .ConfigureAwait(false);

                    if (response.Code == 0 || response.Code == 200)
                    {
                        cancelledCount++;
                        _logger.LogDebug("Cancelled order {OrderId}", orderId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to cancel order {OrderId}: {Message}",
                            orderId, response.Message);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Exception cancelling order {OrderId}", orderId);
                }
            }
        }
        finally
        {
            _orderLock.Release();
        }

        _logger.LogInformation(
            "Cancelled {Count}/{Total} orders",
            cancelledCount, orderIds.Count);

        return cancelledCount;
    }

    /// <inheritdoc />
    public async Task<int> CancelAllGridOrdersAsync(int marketId, CancellationToken ct = default)
    {
        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var response = await _commandClient.CancelAllOrdersAsync(marketId, timeInForce: 0, ct)
                .ConfigureAwait(false);

            if (response.Code == 0 || response.Code == 200)
            {
                _logger.LogInformation("Cancelled all orders for market {MarketId}", marketId);
                // Note: We don't know exact count from cancel all response
                return -1; // Indicates success but unknown count
            }

            _logger.LogWarning(
                "Failed to cancel all orders for market {MarketId}: {Message}",
                marketId, response.Message);
            return 0;
        }
        finally
        {
            _orderLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SyncOrderStatusAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            return;
        }

        try
        {
            // Get auth token for authenticated API call
            var (authToken, authError) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
            if (authError != null || string.IsNullOrEmpty(authToken))
            {
                _logger.LogError("Failed to create auth token: {Error}", authError ?? "empty token");
                throw new InvalidOperationException($"Failed to create auth token: {authError ?? "empty token"}");
            }

            var activeOrders = await _queryClient.GetActiveOrdersAsync(AccountIndex, marketId, authToken, ct)
                .ConfigureAwait(false);

            // Build lookup by client order index for matching
            var orderLookup = activeOrders
                .Where(o => o.ClientOrderIndex.HasValue)
                .ToDictionary(o => o.ClientOrderIndex!.Value, o => o);

            foreach (var level in levels)
            {
                if (!level.ClientOrderIndex.HasValue)
                {
                    continue;
                }

                if (orderLookup.TryGetValue(level.ClientOrderIndex.Value, out var order))
                {
                    // Order is still active
                    level.OrderId = long.TryParse(order.OrderId, out var id) ? id : null;
                    level.Status = GridLevelStatus.Active;
                }
                else if (level.Status == GridLevelStatus.Active)
                {
                    // Order was active but no longer in active orders - likely filled
                    level.Status = GridLevelStatus.Filled;
                    _logger.LogInformation(
                        "Detected fill at {Side} level {Index}, price {Price}",
                        level.IsBid ? "bid" : "ask",
                        level.LevelIndex,
                        level.Price);
                }

                level.LastUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync order status for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<decimal> CalculateOrderSizeAsync(
        int marketId,
        decimal price,
        int totalLevels,
        CancellationToken ct = default)
    {
        // Get account balance for position sizing
        try
        {
            var account = await _queryClient.GetAccountAsync(AccountIndex, ct)
                .ConfigureAwait(false);

            // Parse available balance (in USDC) - API returns human-readable values, no scaling needed
            var availableBalance = decimal.TryParse(account.AvailableBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance)
                ? balance
                : 0m;

            if (availableBalance <= 0)
            {
                _logger.LogWarning("No available balance for order sizing");
                return _config.Capital.MinOrderSizeUsd / price;
            }

            // Calculate max capital to deploy
            var maxDeployable = availableBalance * (_config.Capital.MaxDeployedCapitalPercent / 100m);

            // Calculate per-level allocation
            var perLevelAllocation = maxDeployable / totalLevels;

            // Apply order size constraints
            var orderSizeUsd = Math.Max(perLevelAllocation, _config.Capital.MinOrderSizeUsd);
            orderSizeUsd = Math.Min(orderSizeUsd, availableBalance * (_config.Capital.MaxOrderSizePercent / 100m));

            // Convert to base asset units
            var orderSize = orderSizeUsd / price;

            _logger.LogDebug(
                "Calculated order size: {Size} at price {Price} (USD value: {UsdValue})",
                orderSize, price, orderSizeUsd);

            return orderSize;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate order size, using minimum");
            return _config.Capital.MinOrderSizeUsd / price;
        }
    }

    /// <inheritdoc />
    public async Task<int> CancelExistingOrdersOnStartupAsync(int marketId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Checking for existing orders on exchange for market {MarketId} before grid initialization",
            marketId);

        try
        {
            // Get auth token for authenticated API call
            var (authToken, authError) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
            if (authError != null || string.IsNullOrEmpty(authToken))
            {
                _logger.LogError("Failed to create auth token for startup cleanup: {Error}", authError ?? "empty token");
                return 0;
            }

            // Query active orders from exchange
            var activeOrders = await _queryClient.GetActiveOrdersAsync(AccountIndex, marketId, authToken, ct)
                .ConfigureAwait(false);

            if (activeOrders.Count == 0)
            {
                _logger.LogInformation("No existing orders found on exchange for market {MarketId}", marketId);
                return 0;
            }

            _logger.LogWarning(
                "Found {Count} existing orders on exchange for market {MarketId}. Cancelling all before grid initialization.",
                activeOrders.Count, marketId);

            // Log order details for debugging
            foreach (var order in activeOrders)
            {
                _logger.LogDebug(
                    "Cancelling existing order: Id={OrderId}, Side={Side}, Price={Price}, Size={Size}",
                    order.OrderId,
                    order.Side,
                    order.Price,
                    order.InitialBaseAmount);
            }

            // Cancel all orders
            var response = await _commandClient.CancelAllOrdersAsync(marketId, timeInForce: 0, ct)
                .ConfigureAwait(false);

            if (response.Code == 0 || response.Code == 200)
            {
                _logger.LogInformation(
                    "Successfully cancelled {Count} existing orders for market {MarketId} on startup",
                    activeOrders.Count, marketId);
                return activeOrders.Count;
            }

            _logger.LogWarning(
                "CancelAllOrders returned non-success code {Code}: {Message}",
                response.Code, response.Message);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to cancel existing orders on startup for market {MarketId}. " +
                "Grid initialization will proceed but may result in duplicate orders.",
                marketId);
            return 0;
        }
    }

    /// <summary>
    /// Fetches the current position size from the exchange for the specified market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current position size (absolute value), or 0 if no position exists.</returns>
    private async Task<decimal> GetCurrentPositionAsync(int marketId, CancellationToken ct)
    {
        try
        {
            var account = await _queryClient.GetAccountAsync(AccountIndex, ct).ConfigureAwait(false);

            if (account.Positions == null || account.Positions.Count == 0)
            {
                return 0m;
            }

            var position = account.Positions.FirstOrDefault(p => p.MarketId == marketId);
            if (position == null)
            {
                return 0m;
            }

            // Parse position size - returns absolute value since sign indicates direction
            if (!decimal.TryParse(position.Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
            {
                _logger.LogWarning(
                    "Failed to parse position size '{Size}' for market {MarketId}",
                    position.Positionn, marketId);
                return 0m;
            }

            return Math.Abs(size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch current position for market {MarketId}",
                marketId);
            return 0m;
        }
    }

    /// <summary>
    /// Generates a unique client order index from timestamp, sequence, and level.
    /// Uses millisecond timestamp + atomic sequence counter to prevent collisions.
    /// </summary>
    private static long GenerateClientOrderIndex(GridLevel level)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sequence = Interlocked.Increment(ref _orderSequence) % 1000;
        var sideIndicator = level.IsBid ? 0 : 1;
        // Format: timestamp(ms) % 10B * 10000 + sequence(0-999) * 10 + side(0-1) * 5 + levelIndex
        return (timestamp % 10_000_000_000) * 10000 + sequence * 10 + sideIndicator * 5 + level.LevelIndex;
    }

    /// <summary>
    /// Disposes the order lock semaphore.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _orderLock.Dispose();
    }
}
