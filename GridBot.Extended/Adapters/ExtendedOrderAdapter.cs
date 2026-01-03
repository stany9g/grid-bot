using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using GridBot.Abstractions.Trading;
using GridBot.Extended.Models.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CreateOrderRequest = GridBot.Abstractions.Models.Orders.CreateOrderRequest;
using ApiCreateOrderRequest = GridBot.Extended.Models.Api.CreateOrderRequest;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Order state for tracking asynchronous order lifecycle.
/// </summary>
internal enum LocalOrderState
{
    /// <summary>Order created locally but not submitted.</summary>
    Created,
    /// <summary>Order submitted, awaiting confirmation.</summary>
    PendingConfirmation,
    /// <summary>Order confirmed active by exchange.</summary>
    Active,
    /// <summary>Order rejected by exchange.</summary>
    Rejected,
    /// <summary>Cancel request sent, awaiting confirmation.</summary>
    PendingCancellation,
    /// <summary>Order cancelled.</summary>
    Cancelled,
    /// <summary>Order filled.</summary>
    Filled
}

/// <summary>
/// Tracks local order state for the order state machine.
/// </summary>
internal sealed class LocalOrderInfo
{
    public required string OrderId { get; init; }
    public required string MarketId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public LocalOrderState State { get; set; }
    public string? ExchangeOrderId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Adapts Extended HTTP client to the <see cref="IOrderClient"/> interface.
/// Implements order state machine: Created -> PendingConfirmation -> Active/Rejected.
/// </summary>
internal sealed class ExtendedOrderAdapter : IOrderClient, IDisposable
{
    private const int CleanupIntervalSeconds = 30;
    private const int IdempotencyWindowMinutes = 1;
    private const int IdempotencyKeyExpiryMinutes = 5;
    private const int MarketCacheExpiryMinutes = 60;

    private readonly IExtendedHttpClient _httpClient;
    private readonly NonceManager _nonceManager;
    private readonly ExtendedScalingAdapter _scalingAdapter;
    private readonly StarkSigner _starkSigner;
    private readonly ExtendedOptions _options;
    private readonly ILogger<ExtendedOrderAdapter> _logger;
    private readonly ConcurrentDictionary<string, LocalOrderInfo> _pendingOrders = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentIdempotencyKeys = new();
    private readonly ConcurrentDictionary<string, MarketInfo> _marketInfoCache = new();
    private DateTimeOffset _marketCacheExpiry = DateTimeOffset.MinValue;
    private Timer? _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedOrderAdapter"/> class.
    /// </summary>
    public ExtendedOrderAdapter(
        IExtendedHttpClient httpClient,
        NonceManager nonceManager,
        ExtendedScalingAdapter scalingAdapter,
        StarkSigner starkSigner,
        IOptions<ExtendedOptions> options,
        ILogger<ExtendedOrderAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _nonceManager = nonceManager ?? throw new ArgumentNullException(nameof(nonceManager));
        _scalingAdapter = scalingAdapter ?? throw new ArgumentNullException(nameof(scalingAdapter));
        _starkSigner = starkSigner ?? throw new ArgumentNullException(nameof(starkSigner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the count of pending orders awaiting confirmation.
    /// </summary>
    public int PendingOrderCount => _pendingOrders.Count(x => x.Value.State == LocalOrderState.PendingConfirmation);

    /// <inheritdoc />
    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY RUN] Would create order: {Side} {Size} {Market} @ {Price}",
                request.Side, request.Size, request.MarketId, request.Price);
            return OrderResult.Success(Guid.NewGuid().ToString(), "DRY_RUN");
        }

        // Check pending order limit
        var pendingCount = _pendingOrders.Count(x =>
            x.Value.MarketId == request.MarketId &&
            x.Value.State == LocalOrderState.PendingConfirmation);

        if (pendingCount >= ExtendedConstants.MaxPendingOrdersPerMarket)
        {
            _logger.LogWarning(
                "Pending order limit reached for {Market}: {Count}/{Max}",
                request.MarketId, pendingCount, ExtendedConstants.MaxPendingOrdersPerMarket);
            return OrderResult.Failure("Pending order limit reached. Wait for confirmations.");
        }

        // Check for duplicate order (idempotency protection)
        var idempotencyKey = GenerateIdempotencyKey(request);
        if (_recentIdempotencyKeys.TryGetValue(idempotencyKey, out var lastSubmitted))
        {
            if (DateTimeOffset.UtcNow - lastSubmitted < TimeSpan.FromMinutes(IdempotencyWindowMinutes))
            {
                _logger.LogWarning(
                    "Duplicate order detected within {Window} minute(s). Market: {Market}, Side: {Side}, Price: {Price}, Size: {Size}",
                    IdempotencyWindowMinutes, request.MarketId, request.Side, request.Price, request.Size);
                return OrderResult.Failure("Duplicate order detected. Wait before retrying with same parameters.");
            }
        }
        _recentIdempotencyKeys[idempotencyKey] = DateTimeOffset.UtcNow;

        // Generate client order ID (outside try for catch access)
        var clientOrderId = Guid.NewGuid().ToString("N");

        try
        {
            // Create local order tracking
            var localOrder = new LocalOrderInfo
            {
                OrderId = clientOrderId,
                MarketId = request.MarketId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = LocalOrderState.Created
            };
            _pendingOrders[clientOrderId] = localOrder;

            // Build Extended-specific request (async for market info fetch)
            var extendedRequest = await BuildOrderRequestAsync(request, clientOrderId, ct);

            // Mark as pending before submission
            localOrder.State = LocalOrderState.PendingConfirmation;

            var response = await _httpClient.CreateOrderAsync(extendedRequest, ct);

            // If we get here, the order was accepted (errors throw ExtendedApiException)
            localOrder.ExchangeOrderId = response.Id.ToString();

            // CRITICAL: HTTP 200 does NOT mean order is active!
            // Keep in PendingConfirmation until WebSocket confirms
            _logger.LogInformation(
                "Order {ClientOrderId} submitted, awaiting confirmation. Exchange ID: {ExchangeId}",
                clientOrderId, response.Id);

            return OrderResult.PendingConfirmation(clientOrderId, response.Id.ToString());
        }
        catch (ExtendedApiException ex)
        {
            if (_pendingOrders.TryGetValue(clientOrderId, out var order))
            {
                order.State = LocalOrderState.Rejected;
                order.ErrorMessage = ex.Message;
            }

            _logger.LogWarning(ex, "Order {ClientOrderId} rejected: {Error}", clientOrderId, ex.Message);
            return OrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BatchOrderResult> CreateOrderBatchAsync(CreateOrderRequest[] requests, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Length == 0)
        {
            return BatchOrderResult.FromResults([]);
        }

        // Extended may not support true batch orders, so we submit sequentially
        // but we could optimize with mass cancel if needed
        var results = new List<OrderResult>();

        foreach (var request in requests)
        {
            var result = await CreateOrderAsync(request, ct);
            results.Add(result);
        }

        return BatchOrderResult.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<OrderResult> CancelOrderAsync(string marketId, string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY RUN] Would cancel order: {OrderId} on {Market}", orderId, marketId);
            return OrderResult.Success(orderId, "DRY_RUN_CANCEL");
        }

        try
        {
            // Mark as pending cancellation BEFORE sending
            if (_pendingOrders.TryGetValue(orderId, out var localOrder))
            {
                localOrder.State = LocalOrderState.PendingCancellation;
            }

            var success = await _httpClient.CancelOrderAsync(orderId, ct);

            if (success)
            {
                // HTTP 200 received, but order may not be cancelled yet
                // Return pending status - WebSocket will confirm actual cancellation
                _logger.LogInformation(
                    "Cancel request sent for order {OrderId}, awaiting confirmation",
                    orderId);

                return OrderResult.PendingConfirmation(orderId, "CANCEL_PENDING");
            }
            else
            {
                // Revert state if HTTP failed
                if (localOrder != null)
                {
                    localOrder.State = LocalOrderState.Active;
                }
                return OrderResult.Failure("Cancel request failed");
            }
        }
        catch (ExtendedApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Order not found - already cancelled or filled
            _logger.LogDebug("Order {OrderId} not found, treating as already cancelled", orderId);

            // Remove from pending orders
            _pendingOrders.TryRemove(orderId, out _);

            return OrderResult.Success(orderId, "ALREADY_CANCELLED");
        }
        catch (ExtendedApiException ex)
        {
            // Revert state on error
            if (_pendingOrders.TryGetValue(orderId, out var localOrder))
            {
                localOrder.State = LocalOrderState.Active;
            }

            _logger.LogError(ex, "Failed to cancel order {OrderId}", orderId);
            return OrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OrderResult> CancelAllOrdersAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY RUN] Would cancel all orders on {Market}", marketId);
            return OrderResult.Success("ALL", "DRY_RUN_CANCEL_ALL");
        }

        try
        {
            var request = new MassCancelRequest
            {
                Market = marketId
            };

            var response = await _httpClient.MassCancelOrdersAsync(request, ct);

            _logger.LogInformation(
                "Mass cancel on {Market}: {Cancelled} orders cancelled",
                marketId, response.CancelledCount);

            // Clear local pending orders for this market
            var toRemove = _pendingOrders
                .Where(x => x.Value.MarketId == marketId)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _pendingOrders.TryRemove(key, out _);
            }

            return OrderResult.Success("ALL", $"CANCELLED_{response.CancelledCount}");
        }
        catch (ExtendedApiException ex)
        {
            _logger.LogError(ex, "Failed to mass cancel orders on {Market}", marketId);
            return OrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<OrderResult> ModifyOrderAsync(ModifyOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Extended DEX doesn't support in-place order modification.
        // Clients should cancel and recreate orders manually.
        _logger.LogWarning(
            "Order modification not supported for Extended DEX. Cancel order {OrderId} and create a new one.",
            request.OrderId);

        return Task.FromResult(OrderResult.Failure(
            "Order modification not supported. Cancel and recreate the order instead."));
    }

    /// <summary>
    /// Confirms an order from WebSocket update.
    /// </summary>
    /// <param name="clientOrderId">Client order ID.</param>
    /// <param name="exchangeOrderId">Exchange order ID.</param>
    public void ConfirmOrder(string clientOrderId, string exchangeOrderId)
    {
        if (_pendingOrders.TryGetValue(clientOrderId, out var order))
        {
            order.State = LocalOrderState.Active;
            order.ExchangeOrderId = exchangeOrderId;
            _logger.LogInformation("Order {ClientOrderId} confirmed as active", clientOrderId);
        }
    }

    /// <summary>
    /// Rejects an order from WebSocket update.
    /// </summary>
    /// <param name="clientOrderId">Client order ID.</param>
    /// <param name="reason">Rejection reason.</param>
    public void RejectOrder(string clientOrderId, string reason)
    {
        if (_pendingOrders.TryGetValue(clientOrderId, out var order))
        {
            order.State = LocalOrderState.Rejected;
            order.ErrorMessage = reason;
            _logger.LogWarning("Order {ClientOrderId} rejected: {Reason}", clientOrderId, reason);
        }
    }

    /// <summary>
    /// Confirms a cancellation from WebSocket update.
    /// </summary>
    /// <param name="clientOrderId">Client order ID.</param>
    public void ConfirmCancellation(string clientOrderId)
    {
        if (_pendingOrders.TryGetValue(clientOrderId, out var order))
        {
            if (order.State == LocalOrderState.PendingCancellation)
            {
                order.State = LocalOrderState.Cancelled;
                _logger.LogInformation("Cancel confirmed for order {ClientOrderId}", clientOrderId);
            }
        }
    }

    /// <summary>
    /// Cleans up orphaned pending orders (older than timeout) and expired idempotency keys.
    /// </summary>
    public void CleanupOrphanedOrders()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-ExtendedConstants.OrphanOrderAgeSeconds);

        // Clean up orphaned pending confirmations and pending cancellations
        var orphaned = _pendingOrders
            .Where(x => (x.Value.State == LocalOrderState.PendingConfirmation ||
                         x.Value.State == LocalOrderState.PendingCancellation) &&
                        x.Value.CreatedAt < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in orphaned)
        {
            if (_pendingOrders.TryRemove(key, out var order))
            {
                var stateStr = order.State == LocalOrderState.PendingConfirmation
                    ? "pending confirmation"
                    : "pending cancellation";
                _logger.LogWarning(
                    "Removed orphaned order {OrderId} ({State}, created {Age:F1}s ago)",
                    key, stateStr, (DateTimeOffset.UtcNow - order.CreatedAt).TotalSeconds);
            }
        }

        // Cleanup expired idempotency keys
        var idempotencyExpiry = DateTimeOffset.UtcNow.AddMinutes(-IdempotencyKeyExpiryMinutes);
        var expiredKeys = _recentIdempotencyKeys
            .Where(x => x.Value < idempotencyExpiry)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _recentIdempotencyKeys.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired idempotency keys", expiredKeys.Count);
        }
    }

    /// <summary>
    /// Generates an idempotency key from order parameters.
    /// </summary>
    private static string GenerateIdempotencyKey(CreateOrderRequest request)
    {
        // Hash of: market + side + price + size + reduceOnly
        var input = $"{request.MarketId}|{request.Side}|{request.Price:F8}|{request.Size:F8}|{request.ReduceOnly}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16]; // First 16 chars
    }

    private async Task<ApiCreateOrderRequest> BuildOrderRequestAsync(CreateOrderRequest request, string clientOrderId, CancellationToken ct)
    {
        // Get market info for L2Config (needed for signing)
        var marketInfo = await GetMarketInfoAsync(request.MarketId, ct);
        if (marketInfo?.L2Config == null)
        {
            throw new InvalidOperationException($"Market {request.MarketId} L2Config not available");
        }

        var l2Config = marketInfo.L2Config;
        var isBuy = request.Side == OrderSide.Buy;

        // Generate random nonce (32-bit) per Python SDK
        var nonce = StarkAmountCalculator.GenerateNonce();

        // Calculate expiry
        var maxExpiry = TimeSpan.FromDays(_options.MaxOrderExpiryDays);
        var expiryMs = DateTimeOffset.UtcNow.Add(maxExpiry).ToUnixTimeMilliseconds();

        // Calculate settlement expiration (order expiry + 14 days, in seconds)
        var settlementExpiration = StarkAmountCalculator.CalcSettlementExpiration(expiryMs);

        // Calculate Stark amounts using market resolutions
        var (baseAmount, quoteAmount, feeAmount) = StarkAmountCalculator.CalculateStarkAmounts(
            request.Size,
            request.Price,
            ExtendedConstants.DefaultFeeRate,
            isBuy,
            l2Config.SyntheticResolution,
            l2Config.CollateralResolution);

        // Parse L2Vault to position ID
        var positionId = long.Parse(_options.L2Vault);

        // Build StarkEx order parameters for signing
        var orderParams = new StarkExOrderParams
        {
            PositionId = positionId,
            BaseAmount = baseAmount,
            QuoteAmount = quoteAmount,
            FeeAmount = feeAmount,
            Nonce = nonce,
            ExpirationSeconds = settlementExpiration
        };

        // Sign the order using Extended DEX algorithm (includes domain params)
        var (r, s) = _starkSigner.SignOrder(orderParams, l2Config, _options.IsTestnet);

        // Format price and quantity for API
        var priceStr = _scalingAdapter.FormatPrice(request.Price, request.MarketId);
        var qtyStr = _scalingAdapter.FormatQuantity(request.Size, request.MarketId);

        _logger.LogDebug(
            "Built order: Market={Market}, Side={Side}, Qty={Qty}, Price={Price}, " +
            "BaseAmount={BaseAmount}, QuoteAmount={QuoteAmount}, Fee={FeeAmount}, " +
            "PositionId={PositionId}, Nonce={Nonce}, Expiration={Expiration}",
            request.MarketId, request.Side, qtyStr, priceStr,
            baseAmount, quoteAmount, feeAmount, positionId, nonce, settlementExpiration);

        // Build settlement object with nested signature per Python SDK
        var settlement = new SettlementObject
        {
            StarkKey = _starkSigner.StarkPublicKey ?? _options.StarkPublicKey,
            CollateralPosition = _options.L2Vault,
            Signature = new SignatureObject
            {
                R = r,
                S = s
            }
        };

        return new ApiCreateOrderRequest
        {
            Id = clientOrderId,
            Market = request.MarketId,
            Type = "LIMIT", // Always LIMIT per Python SDK
            Side = isBuy ? "BUY" : "SELL",
            Qty = qtyStr,
            Price = priceStr,
            Fee = ExtendedConstants.DefaultFeeRate.ToString(CultureInfo.InvariantCulture),
            ExpiryEpochMillis = expiryMs,
            TimeInForce = MapTimeInForce(request.TimeInForce),
            ReduceOnly = request.ReduceOnly,
            PostOnly = false,
            Nonce = nonce.ToString(),
            SelfTradeProtectionLevel = "ACCOUNT",
            Settlement = settlement
        };
    }

    /// <summary>
    /// Gets market info from cache or API.
    /// </summary>
    private async Task<MarketInfo?> GetMarketInfoAsync(string marketId, CancellationToken ct)
    {
        // Check if cache is expired
        if (DateTimeOffset.UtcNow > _marketCacheExpiry)
        {
            _marketInfoCache.Clear();
            _marketCacheExpiry = DateTimeOffset.UtcNow.AddMinutes(MarketCacheExpiryMinutes);
        }

        // Check cache first
        if (_marketInfoCache.TryGetValue(marketId, out var cachedInfo))
        {
            return cachedInfo;
        }

        // Fetch from API
        try
        {
            var markets = await _httpClient.GetMarketsAsync(ct);
            foreach (var market in markets)
            {
                _marketInfoCache[market.Name] = market;
            }

            return _marketInfoCache.GetValueOrDefault(marketId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch market info for {MarketId}", marketId);
            return null;
        }
    }

    private static string MapTimeInForce(TimeInForce tif)
    {
        return tif switch
        {
            TimeInForce.GoodTillCancel => "GTT",
            TimeInForce.ImmediateOrCancel => "IOC",
            TimeInForce.FillOrKill => "FOK",
            TimeInForce.PostOnly => "POST_ONLY",
            _ => "GTT"
        };
    }

    /// <summary>
    /// Starts the automatic orphan order cleanup timer.
    /// </summary>
    public void StartCleanupTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cleanupTimer = new Timer(
            _ => CleanupOrphanedOrders(),
            null,
            TimeSpan.FromSeconds(CleanupIntervalSeconds),
            TimeSpan.FromSeconds(CleanupIntervalSeconds));

        _logger.LogInformation("Started orphan order cleanup timer (interval: {Interval}s)", CleanupIntervalSeconds);
    }

    /// <summary>
    /// Stops the automatic orphan order cleanup timer.
    /// </summary>
    public void StopCleanupTimer()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        _logger.LogInformation("Stopped orphan order cleanup timer");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopCleanupTimer();
    }
}
