using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// Decorator that wraps ILighterCommandClient and logs operations instead of executing them.
/// Used for testing WebSocket data feeds without creating real orders.
/// </summary>
internal sealed class DryRunCommandClient : ILighterCommandClient
{
    private readonly ILighterCommandClient _inner;
    private readonly ILogger<DryRunCommandClient> _logger;

    public DryRunCommandClient(ILighterCommandClient inner, ILogger<DryRunCommandClient> logger)
    {
        _inner = inner;
        _logger = logger;
        _logger.LogWarning("DRY RUN MODE ENABLED - No orders will be created, modified, or cancelled");
    }

    public Task<RespSendTx> CreateOrderAsync(CreateOrderRequest request, bool? priceProtection = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CreateOrder: Market={Market}, Side={Side}, Price={Price}, Amount={Amount}",
            request.MarketIndex, request.IsAsk ? "SELL" : "BUY", request.Price, request.BaseAmount);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> CreateMarketOrderAsync(MarketOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CreateMarketOrder: Market={Market}, Side={Side}, Amount={Amount}",
            request.MarketIndex, request.IsAsk ? "SELL" : "BUY", request.BaseAmount);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> CreateGroupedOrdersAsync(CreateGroupedOrdersRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CreateGroupedOrders: Type={Type}", request.GroupingType);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> CancelOrderAsync(int marketId, long orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CancelOrder: Market={Market}, OrderId={OrderId}", marketId, orderId);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> CancelAllOrdersAsync(int marketId, long cancelTimestampMs = 0, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CancelAllOrders: Market={Market}", marketId);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> ModifyOrderAsync(ModifyOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] ModifyOrder: Market={Market}, OrderId={OrderId}", request.MarketIndex, request.OrderId);
        return Task.FromResult(DryRunResponse());
    }

    public Task<RespSendTx> UpdateLeverageAsync(UpdateLeverageRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] UpdateLeverage: Market={Market}, InitialMargin={InitialMargin}", request.MarketIndex, request.InitialMarginFraction);
        return Task.FromResult(DryRunResponse());
    }

    public Task<SignedOrderResult> SignOrderAsync(CreateOrderRequest request)
    {
        _logger.LogInformation("[DRY RUN] SignOrder: Market={Market}, Side={Side}", request.MarketIndex, request.IsAsk ? "SELL" : "BUY");
        return Task.FromResult(new SignedOrderResult(0, "dry-run", null));
    }

    public Task<BatchOrderResult> SubmitOrderBatchAsync(SignedOrderResult[] signedOrders, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] SubmitOrderBatch: Count={Count}", signedOrders.Length);
        return Task.FromResult(new BatchOrderResult { IsSuccess = true, OrdersSubmitted = signedOrders.Length, TxHashes = [] });
    }

    public Task<BatchOrderResult> CreateOrderBatchAsync(CreateOrderRequest[] requests, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DRY RUN] CreateOrderBatch: Count={Count}", requests.Length);
        return Task.FromResult(new BatchOrderResult { IsSuccess = true, OrdersSubmitted = requests.Length, TxHashes = [] });
    }

    // Pass-through for read operations
    public Task<long> SyncNonceAsync(long accountIndex, int apiKeyIndex, CancellationToken cancellationToken = default)
        => _inner.SyncNonceAsync(accountIndex, apiKeyIndex, cancellationToken);

    public Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600)
        => _inner.CreateAuthTokenAsync(validitySeconds);

    public void Dispose() => _inner.Dispose();

    private static RespSendTx DryRunResponse() => new()
    {
        Code = 0,
        Message = "DRY RUN - Not executed",
        TxHash = "dry-run-" + Guid.NewGuid().ToString("N")[..8],
        PredictedExecutionTimeMs = 0
    };
}
