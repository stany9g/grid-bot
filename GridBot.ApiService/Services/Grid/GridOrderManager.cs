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
    /// Circuit breaker threshold - stop placing orders after this many consecutive failures.
    /// </summary>
    private const int CircuitBreakerThreshold = 3;

    /// <summary>
    /// Lighter DEX maker fee rate (0.02%).
    /// </summary>
    private const decimal MakerFeeRate = 0.0002m;

    /// <summary>
    /// Post-Only rejection error codes that indicate the order would cross the spread.
    /// </summary>
    private static readonly HashSet<int> PostOnlyRejectionCodes = [4001, 4002, 4003]; // Placeholder codes - verify with Lighter docs

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

        var errors = new List<GridOrderError>();
        var ordersToPlace = new List<(GridLevel Level, CreateOrderRequest Request, long ClientOrderIndex)>();

        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Phase 1: Prepare all orders (validation, moon bag checks, scaling)
            decimal? cachedPosition = null;

            foreach (var level in levels)
            {
                if (level.Status == GridLevelStatus.Active || level.Status == GridLevelStatus.Filled)
                {
                    continue; // Skip already placed or filled orders
                }

                ct.ThrowIfCancellationRequested();

                // HIGH-005 FIX: Check moon bag protection before placing sell orders
                if (!level.IsBid) // This is a sell order (ask)
                {
                    cachedPosition ??= await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);
                    var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(
                        marketId, level.Size, cachedPosition.Value, ct).ConfigureAwait(false);

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
                    continue;
                }

                ordersToPlace.Add((level, request, clientOrderIndex));
            }

            // Phase 2: Submit all orders in a single batch
            if (ordersToPlace.Count == 0)
            {
                _logger.LogInformation("No valid orders to place after validation");
                return new GridPlacementResult
                {
                    OrdersPlaced = 0,
                    OrdersFailed = errors.Count,
                    Errors = errors
                };
            }

            _logger.LogInformation(
                "Submitting batch of {Count} orders for market {MarketId}",
                ordersToPlace.Count, marketId);

            var requests = ordersToPlace.Select(o => o.Request).ToArray();
            var batchResult = await _commandClient.CreateOrderBatchAsync(requests, ct).ConfigureAwait(false);

            int ordersPlaced;
            int ordersFailed;

            if (batchResult.IsSuccess)
            {
                // Mark all orders as active
                for (var i = 0; i < ordersToPlace.Count; i++)
                {
                    var (level, _, clientOrderIndex) = ordersToPlace[i];
                    level.ClientOrderIndex = clientOrderIndex;
                    level.Status = GridLevelStatus.Active;
                    level.LastUpdatedAt = DateTimeOffset.UtcNow;
                    level.OriginalSize = level.Size;

                    var txHash = i < batchResult.TxHashes.Length ? batchResult.TxHashes[i] : "N/A";
                    _logger.LogDebug(
                        "Placed {Side} order at {Price} (level {Index}), TxHash: {TxHash}",
                        level.IsBid ? "bid" : "ask",
                        level.Price,
                        level.LevelIndex,
                        txHash);
                }

                ordersPlaced = ordersToPlace.Count;
                ordersFailed = errors.Count;

                _logger.LogInformation(
                    "Batch order placement successful: {Placed} orders placed in single request",
                    ordersPlaced);
            }
            else
            {
                // Batch failed - all orders failed
                _logger.LogError(
                    "Batch order placement failed: {Error} (code {Code})",
                    batchResult.ErrorMessage, batchResult.Code);

                foreach (var (level, _, _) in ordersToPlace)
                {
                    errors.Add(new GridOrderError
                    {
                        LevelIndex = level.LevelIndex,
                        Price = level.Price,
                        IsBid = level.IsBid,
                        ErrorMessage = batchResult.ErrorMessage ?? $"Batch failed with code {batchResult.Code}"
                    });
                }

                ordersPlaced = 0;
                ordersFailed = errors.Count;
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exception during batch order placement for market {MarketId}", marketId);

            // Mark all pending orders as failed
            foreach (var (level, _, _) in ordersToPlace)
            {
                errors.Add(new GridOrderError
                {
                    LevelIndex = level.LevelIndex,
                    Price = level.Price,
                    IsBid = level.IsBid,
                    ErrorMessage = ex.Message
                });
            }

            return new GridPlacementResult
            {
                OrdersPlaced = 0,
                OrdersFailed = errors.Count,
                Errors = errors
            };
        }
        finally
        {
            _orderLock.Release();
        }
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

    /// <summary>
    /// Error code for "invalid tx info" - often means no orders to cancel.
    /// </summary>
    private const int LighterErrorInvalidTxInfo = 21501;

    /// <summary>
    /// Error code for "account has queued cancel all request" - previous cancel still pending.
    /// </summary>
    private const int LighterErrorQueuedCancelAll = 21712;

    /// <inheritdoc />
    public async Task<int> CancelAllGridOrdersAsync(int marketId, CancellationToken ct = default)
    {
        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var response = await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, ct)
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
        catch (LighterApiException ex) when (ex.Code == LighterErrorInvalidTxInfo)
        {
            // Error 21501 "invalid tx info" typically means no orders to cancel
            // This is not a failure state - just means the grid is already clear
            _logger.LogInformation(
                "CancelAllOrders returned 21501 for market {MarketId} - treating as no orders to cancel",
                marketId);
            return 0;
        }
        catch (LighterApiException ex) when (ex.Code == LighterErrorQueuedCancelAll)
        {
            // Error 21712 means a previous cancel-all is still pending
            _logger.LogWarning(
                "Cancel all orders already queued for market {MarketId} - previous request still processing",
                marketId);
            // Return success since cancellation is in progress
            return -1;
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

            // DIAGNOSTIC: Log order sync details for debugging fill detection issues
            _logger.LogDebug(
                "SyncOrderStatus for market {MarketId}: {TotalOrders} active orders from exchange, {LevelsCount} grid levels to sync",
                marketId, activeOrders.Count, levels.Count);

            // Build lookup by client order index for matching
            var orderLookup = activeOrders
                .Where(o => o.ClientOrderIndex.HasValue)
                .ToDictionary(o => o.ClientOrderIndex!.Value, o => o);

            // DIAGNOSTIC: Log if there's a mismatch that could indicate problems
            var ordersWithoutClientIndex = activeOrders.Count - orderLookup.Count;
            if (ordersWithoutClientIndex > 0)
            {
                _logger.LogWarning(
                    "SyncOrderStatus for market {MarketId}: {Count} orders missing ClientOrderIndex - may cause incorrect fill detection",
                    marketId, ordersWithoutClientIndex);
            }

            var activeLevelsCount = levels.Count(l => l.Status == GridLevelStatus.Active && l.ClientOrderIndex.HasValue);

            // FIX: Prevent false fill detection when WebSocket data is stale or missing
            // If we have active grid levels but exchange returns 0 orders with ClientOrderIndex,
            // this indicates a WebSocket data issue - DO NOT mark orders as filled
            if (activeLevelsCount > 0 && orderLookup.Count == 0)
            {
                _logger.LogWarning(
                    "Order sync skipped for market {MarketId}: {ActiveLevels} active grid levels but exchange returned 0 orders with ClientOrderIndex. " +
                    "WebSocket may be stale or disconnected. Preserving current order state to prevent false fills.",
                    marketId, activeLevelsCount);
                return; // Exit early - do not modify order states
            }

            // FIX CRITICAL: Acquire _orderLock before mutating GridLevel objects
            // This ensures thread-safety with PlaceGridOrdersAsync and ResetFilledLevelsToPendingAsync
            await _orderLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
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

                        // FIX Finding 10: Track partial fills
                        // Compare InitialBaseAmount vs RemainingBaseAmount from API
                        if (decimal.TryParse(order.InitialBaseAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var initialAmount) &&
                            decimal.TryParse(order.RemainingBaseAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var remainingAmount) &&
                            initialAmount > 0)
                        {
                            var filledAmount = initialAmount - remainingAmount;
                            var fillPercent = (filledAmount / initialAmount) * 100m;

                            if (fillPercent > 0 && fillPercent != level.PartialFillPercent)
                            {
                                level.PartialFillPercent = fillPercent;
                                level.Size = remainingAmount; // Update size to remaining amount

                                if (fillPercent > 0)
                                {
                                    _logger.LogInformation(
                                        "Partial fill detected at {Side} level {Index}: {FillPercent:F1}% filled ({Filled}/{Initial})",
                                        level.IsBid ? "bid" : "ask",
                                        level.LevelIndex,
                                        fillPercent,
                                        filledAmount,
                                        initialAmount);
                                }
                            }
                        }
                    }
                    else if (level.Status == GridLevelStatus.Active)
                    {
                        // Order was active but no longer in active orders - fully filled
                        level.Status = GridLevelStatus.Filled;
                        level.PartialFillPercent = 100m; // Mark as fully filled
                        _logger.LogInformation(
                            "Detected fill at {Side} level {Index}, price {Price}",
                            level.IsBid ? "bid" : "ask",
                            level.LevelIndex,
                            level.Price);
                    }

                    level.LastUpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                _orderLock.Release();
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

            // FIX Finding 4: Account for trading fees (entry + exit)
            // Effective deployable capital is reduced by expected fees on both sides of the trade
            var effectiveDeployable = maxDeployable / (1 + MakerFeeRate * 2);

            // Calculate per-level allocation
            var perLevelAllocation = effectiveDeployable / totalLevels;

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

    /// <summary>
    /// Timeout for API calls during startup cleanup (10 seconds).
    /// </summary>
    private static readonly TimeSpan StartupApiTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum retry attempts for transient failures during startup cleanup.
    /// </summary>
    private const int MaxStartupRetries = 3;

    /// <summary>
    /// Delay between retry attempts (doubles each retry: 500ms, 1000ms, 2000ms).
    /// </summary>
    private const int BaseRetryDelayMs = 500;

    /// <inheritdoc />
    public async Task<(bool Success, int CancelledCount)> CancelExistingOrdersOnStartupAsync(int marketId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Checking for existing orders on exchange for market {MarketId} before grid initialization",
            marketId);

        for (var attempt = 1; attempt <= MaxStartupRetries; attempt++)
        {
            var result = await TryCancelExistingOrdersAsync(marketId, attempt, ct).ConfigureAwait(false);

            if (result.Success)
            {
                return result;
            }

            // Don't retry on final attempt
            if (attempt < MaxStartupRetries)
            {
                var delayMs = BaseRetryDelayMs * (1 << (attempt - 1)); // Exponential backoff
                _logger.LogWarning(
                    "Startup order cancellation attempt {Attempt}/{MaxAttempts} failed for market {MarketId}. " +
                    "Retrying in {DelayMs}ms...",
                    attempt, MaxStartupRetries, marketId, delayMs);

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        _logger.LogError(
            "All {MaxAttempts} startup order cancellation attempts failed for market {MarketId}. " +
            "Grid initialization will be blocked to prevent order accumulation.",
            MaxStartupRetries, marketId);
        return (false, 0);
    }

    /// <summary>
    /// Single attempt to cancel existing orders with timeout and verification.
    /// </summary>
    private async Task<(bool Success, int CancelledCount)> TryCancelExistingOrdersAsync(
        int marketId,
        int attemptNumber,
        CancellationToken ct)
    {
        try
        {
            // Create timeout-linked token for API calls
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(StartupApiTimeout);
            var timeoutToken = timeoutCts.Token;

            // Get auth token for authenticated API call
            var (authToken, authError) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
            if (authError != null || string.IsNullOrEmpty(authToken))
            {
                _logger.LogError(
                    "Attempt {Attempt}: Failed to create auth token for startup cleanup: {Error}",
                    attemptNumber, authError ?? "empty token");
                return (false, 0);
            }

            // Query active orders from exchange (with timeout)
            var activeOrders = await _queryClient.GetActiveOrdersAsync(AccountIndex, marketId, authToken, timeoutToken)
                .ConfigureAwait(false);

            if (activeOrders.Count == 0)
            {
                _logger.LogInformation("No existing orders found on exchange for market {MarketId}", marketId);
                return (true, 0);
            }

            var orderCount = activeOrders.Count;
            _logger.LogWarning(
                "Found {Count} existing orders on exchange for market {MarketId}. Cancelling all before grid initialization.",
                orderCount, marketId);

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
            var response = await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, timeoutToken)
                .ConfigureAwait(false);

            if (response.Code != 0 && response.Code != 200)
            {
                _logger.LogError(
                    "Attempt {Attempt}: CancelAllOrders returned non-success code {Code}: {Message}",
                    attemptNumber, response.Code, response.Message);
                return (false, 0);
            }

            // VERIFICATION: Re-query to confirm cancellation actually succeeded
            // This guards against exchange reporting success but not actually cancelling
            // Lighter is a ZK-rollup - cancellations need time to be committed
            var executionTimeMs = response.PredictedExecutionTimeMs;

            // Use predicted execution time + buffer, minimum 1 second, max 5 seconds
            var verificationDelayMs = Math.Clamp(executionTimeMs + 500, 1000, 5000);

            _logger.LogDebug(
                "Waiting {DelayMs}ms for cancellation to be committed (predicted: {PredictedMs}ms) for market {MarketId}...",
                verificationDelayMs, executionTimeMs, marketId);

            await Task.Delay((int)verificationDelayMs, timeoutToken).ConfigureAwait(false);

            // Get fresh auth token for verification query
            var (verifyAuthToken, verifyAuthError) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
            if (verifyAuthError != null || string.IsNullOrEmpty(verifyAuthToken))
            {
                _logger.LogWarning(
                    "Attempt {Attempt}: Could not verify cancellation (auth token failed), " +
                    "but cancel request succeeded. Proceeding cautiously.",
                    attemptNumber);
                // Cancel succeeded, verification failed - accept this
                return (true, orderCount);
            }

            var remainingOrders = await _queryClient.GetActiveOrdersAsync(AccountIndex, marketId, verifyAuthToken, timeoutToken)
                .ConfigureAwait(false);

            if (remainingOrders.Count > 0)
            {
                _logger.LogError(
                    "Attempt {Attempt}: Cancellation verification FAILED for market {MarketId}. " +
                    "Exchange reported success but {RemainingCount} orders still exist.",
                    attemptNumber, marketId, remainingOrders.Count);
                return (false, 0);
            }

            _logger.LogInformation(
                "Successfully cancelled and verified {Count} existing orders for market {MarketId} on startup",
                orderCount, marketId);
            return (true, orderCount);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (not user cancellation)
            _logger.LogError(
                "Attempt {Attempt}: Startup order cancellation timed out after {Timeout}s for market {MarketId}",
                attemptNumber, StartupApiTimeout.TotalSeconds, marketId);
            return (false, 0);
        }
        catch (OperationCanceledException)
        {
            // User cancellation - re-throw
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Attempt {Attempt}: Failed to cancel existing orders on startup for market {MarketId}",
                attemptNumber, marketId);
            return (false, 0);
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

    /// <inheritdoc />
    /// <remarks>
    /// FIX Finding 1: This method provides thread-safe level mutation by using the
    /// order manager's internal lock. This ensures that GridLifecycleService can
    /// reset levels without creating a race condition with order placement.
    /// </remarks>
    public async Task<int> ResetFilledLevelsToPendingAsync(IReadOnlyList<GridLevel> levels, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            return 0;
        }

        await _orderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var resetCount = 0;
            foreach (var level in levels)
            {
                if (level.Status == GridLevelStatus.Filled)
                {
                    level.Status = GridLevelStatus.Pending;
                    level.OrderId = null;
                    level.ClientOrderIndex = null;
                    level.PartialFillPercent = 0m;
                    level.LastUpdatedAt = DateTimeOffset.UtcNow;
                    resetCount++;
                }
            }

            if (resetCount > 0)
            {
                _logger.LogDebug("Reset {Count} filled levels to pending under order lock", resetCount);
            }

            return resetCount;
        }
        finally
        {
            _orderLock.Release();
        }
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
