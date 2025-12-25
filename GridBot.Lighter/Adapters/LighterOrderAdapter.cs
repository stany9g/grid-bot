using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;
using AbstractionsOrderType = GridBot.Abstractions.Models.Enums.OrderType;
using LighterOrderType = GridBot.Lighter.Models.OrderType;
using LighterTimeInForce = GridBot.Lighter.Models.TimeInForce;
using AbstractionsOrderResult = GridBot.Abstractions.Models.Orders.OrderResult;
using AbstractionsBatchResult = GridBot.Abstractions.Models.Orders.BatchOrderResult;
using AbstractionsCreateOrderRequest = GridBot.Abstractions.Models.Orders.CreateOrderRequest;
using AbstractionsModifyOrderRequest = GridBot.Abstractions.Models.Orders.ModifyOrderRequest;
using LighterCreateOrderRequest = GridBot.Lighter.Models.CreateOrderRequest;
using LighterModifyOrderRequest = GridBot.Lighter.Models.ModifyOrderRequest;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="ILighterCommandClient"/> to the <see cref="IOrderClient"/> interface.
/// Handles conversion between abstraction types and Lighter-specific types.
/// </summary>
internal sealed class LighterOrderAdapter : IOrderClient
{
    private readonly ILighterCommandClient _commandClient;
    private readonly IScalingProvider _scalingProvider;
    private readonly LighterMarketMapper _marketMapper;
    private readonly ILogger<LighterOrderAdapter> _logger;
    private long _clientOrderIndex;
    private readonly object _indexLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterOrderAdapter"/> class.
    /// </summary>
    /// <param name="commandClient">The Lighter command client.</param>
    /// <param name="scalingProvider">The scaling provider for price/size conversion.</param>
    /// <param name="marketMapper">The market ID mapper.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterOrderAdapter(
        ILighterCommandClient commandClient,
        IScalingProvider scalingProvider,
        LighterMarketMapper marketMapper,
        ILogger<LighterOrderAdapter> logger)
    {
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
        _scalingProvider = scalingProvider ?? throw new ArgumentNullException(nameof(scalingProvider));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize with timestamp-based client order index
        _clientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <inheritdoc />
    public async Task<AbstractionsOrderResult> CreateOrderAsync(AbstractionsCreateOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var lighterId = _marketMapper.ToLighterId(request.MarketId);
            var scaling = await _scalingProvider.GetMarketScalingAsync(request.MarketId, ct);

            var lighterRequest = new LighterCreateOrderRequest
            {
                MarketIndex = lighterId,
                ClientOrderIndex = GetNextClientOrderIndex(request.ClientOrderId),
                BaseAmount = _scalingProvider.ScaleAmount(request.Size, scaling),
                Price = _scalingProvider.ScalePrice(request.Price, scaling),
                IsAsk = request.Side == OrderSide.Sell,
                OrderType = MapOrderType(request.Type),
                TimeInForce = MapTimeInForce(request.TimeInForce),
                ReduceOnly = request.ReduceOnly,
                TriggerPrice = request.TriggerPrice.HasValue
                    ? (int)_scalingProvider.ScalePrice(request.TriggerPrice.Value, scaling)
                    : OrderConstants.NilTriggerPrice,
                OrderExpiry = request.Expiry.HasValue
                    ? request.Expiry.Value.ToUnixTimeSeconds()
                    : OrderConstants.Default28DayOrderExpiry
            };

            _logger.LogDebug(
                "Creating order: Market={MarketId}, Side={Side}, Price={Price}, Size={Size}",
                request.MarketId, request.Side, request.Price, request.Size);

            var response = await _commandClient.CreateOrderAsync(lighterRequest, cancellationToken: ct);

            return AbstractionsOrderResult.Success(
                lighterRequest.ClientOrderIndex.ToString(),
                response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogWarning(ex, "Order creation failed: {Message}", ex.Message);
            return AbstractionsOrderResult.Failure(ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating order");
            return AbstractionsOrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AbstractionsBatchResult> CreateOrderBatchAsync(AbstractionsCreateOrderRequest[] requests, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Length == 0)
        {
            return AbstractionsBatchResult.FromResults(Array.Empty<AbstractionsOrderResult>());
        }

        try
        {
            var lighterRequests = new LighterCreateOrderRequest[requests.Length];

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                var lighterId = _marketMapper.ToLighterId(request.MarketId);
                var scaling = await _scalingProvider.GetMarketScalingAsync(request.MarketId, ct);

                lighterRequests[i] = new Models.CreateOrderRequest
                {
                    MarketIndex = lighterId,
                    ClientOrderIndex = GetNextClientOrderIndex(request.ClientOrderId),
                    BaseAmount = _scalingProvider.ScaleAmount(request.Size, scaling),
                    Price = _scalingProvider.ScalePrice(request.Price, scaling),
                    IsAsk = request.Side == OrderSide.Sell,
                    OrderType = MapOrderType(request.Type),
                    TimeInForce = MapTimeInForce(request.TimeInForce),
                    ReduceOnly = request.ReduceOnly,
                    TriggerPrice = request.TriggerPrice.HasValue
                        ? (int)_scalingProvider.ScalePrice(request.TriggerPrice.Value, scaling)
                        : OrderConstants.NilTriggerPrice,
                    OrderExpiry = request.Expiry.HasValue
                        ? request.Expiry.Value.ToUnixTimeSeconds()
                        : OrderConstants.Default28DayOrderExpiry
                };
            }

            _logger.LogDebug("Creating batch of {Count} orders", requests.Length);

            var batchResult = await _commandClient.CreateOrderBatchAsync(lighterRequests, ct);

            var results = new List<AbstractionsOrderResult>();
            for (int i = 0; i < requests.Length; i++)
            {
                if (batchResult.IsSuccess && i < batchResult.TxHashes.Length)
                {
                    results.Add(AbstractionsOrderResult.Success(
                        lighterRequests[i].ClientOrderIndex.ToString(),
                        batchResult.TxHashes[i]));
                }
                else
                {
                    results.Add(AbstractionsOrderResult.Failure(
                        batchResult.ErrorMessage ?? "Batch submission failed",
                        batchResult.Code));
                }
            }

            return AbstractionsBatchResult.FromResults(results, batchResult.TxHashes.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating order batch");
            var failedResults = requests
                .Select(_ => AbstractionsOrderResult.Failure(ex.Message))
                .ToList();
            return AbstractionsBatchResult.FromResults(failedResults);
        }
    }

    /// <inheritdoc />
    public async Task<AbstractionsOrderResult> CancelOrderAsync(string marketId, string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        try
        {
            var lighterId = _marketMapper.ToLighterId(marketId);
            var orderIdLong = long.Parse(orderId);

            _logger.LogDebug("Cancelling order: Market={MarketId}, OrderId={OrderId}", marketId, orderId);

            var response = await _commandClient.CancelOrderAsync(lighterId, orderIdLong, ct);

            return AbstractionsOrderResult.Success(orderId, response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogWarning(ex, "Order cancellation failed: {Message}", ex.Message);
            return AbstractionsOrderResult.Failure(ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error cancelling order");
            return AbstractionsOrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AbstractionsOrderResult> CancelAllOrdersAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        try
        {
            var lighterId = _marketMapper.ToLighterId(marketId);

            _logger.LogDebug("Cancelling all orders for market: {MarketId}", marketId);

            var response = await _commandClient.CancelAllOrdersAsync(lighterId, 0, ct);

            return AbstractionsOrderResult.Success("all", response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogWarning(ex, "Cancel all orders failed: {Message}", ex.Message);
            return AbstractionsOrderResult.Failure(ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error cancelling all orders");
            return AbstractionsOrderResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AbstractionsOrderResult> ModifyOrderAsync(AbstractionsModifyOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var lighterId = _marketMapper.ToLighterId(request.MarketId);
            var scaling = await _scalingProvider.GetMarketScalingAsync(request.MarketId, ct);
            var orderIdLong = long.Parse(request.OrderId);

            // For Lighter, we need to provide all values - use current if not specified
            // This is a simplification; in production, we might fetch current order values
            var lighterRequest = new LighterModifyOrderRequest
            {
                MarketIndex = lighterId,
                OrderId = orderIdLong,
                NewBaseAmount = request.NewSize.HasValue
                    ? _scalingProvider.ScaleAmount(request.NewSize.Value, scaling)
                    : 0,
                NewPrice = request.NewPrice.HasValue
                    ? _scalingProvider.ScalePrice(request.NewPrice.Value, scaling)
                    : 0,
                NewTriggerPrice = request.NewTriggerPrice.HasValue
                    ? _scalingProvider.ScalePrice(request.NewTriggerPrice.Value, scaling)
                    : 0
            };

            _logger.LogDebug(
                "Modifying order: Market={MarketId}, OrderId={OrderId}",
                request.MarketId, request.OrderId);

            var response = await _commandClient.ModifyOrderAsync(lighterRequest, ct);

            return AbstractionsOrderResult.Success(request.OrderId, response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogWarning(ex, "Order modification failed: {Message}", ex.Message);
            return AbstractionsOrderResult.Failure(ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error modifying order");
            return AbstractionsOrderResult.Failure(ex.Message);
        }
    }

    private long GetNextClientOrderIndex(string clientOrderId)
    {
        // If the client provides a parseable long, use it
        if (long.TryParse(clientOrderId, out var clientIndex))
        {
            return clientIndex;
        }

        // Otherwise generate a sequential index
        lock (_indexLock)
        {
            return ++_clientOrderIndex;
        }
    }

    private static LighterOrderType MapOrderType(AbstractionsOrderType type)
    {
        return type switch
        {
            AbstractionsOrderType.Limit => LighterOrderType.Limit,
            AbstractionsOrderType.Market => LighterOrderType.Market,
            AbstractionsOrderType.StopLoss => LighterOrderType.StopLoss,
            AbstractionsOrderType.StopLossLimit => LighterOrderType.StopLossLimit,
            AbstractionsOrderType.TakeProfit => LighterOrderType.TakeProfit,
            AbstractionsOrderType.TakeProfitLimit => LighterOrderType.TakeProfitLimit,
            _ => LighterOrderType.Limit
        };
    }

    private static LighterTimeInForce MapTimeInForce(Abstractions.Models.Enums.TimeInForce tif)
    {
        return tif switch
        {
            Abstractions.Models.Enums.TimeInForce.ImmediateOrCancel => LighterTimeInForce.ImmediateOrCancel,
            Abstractions.Models.Enums.TimeInForce.GoodTillCancel => LighterTimeInForce.GoodTillTime,
            Abstractions.Models.Enums.TimeInForce.PostOnly => LighterTimeInForce.PostOnly,
            Abstractions.Models.Enums.TimeInForce.FillOrKill => LighterTimeInForce.ImmediateOrCancel, // Lighter doesn't have FOK, use IOC
            _ => LighterTimeInForce.GoodTillTime
        };
    }
}
