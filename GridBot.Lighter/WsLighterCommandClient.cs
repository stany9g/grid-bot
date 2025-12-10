using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// WebSocket-integrated command client for Lighter operations.
/// Uses ILighterRealtimeState for market data and HTTP POST for transaction submission.
/// </summary>
public sealed class WsLighterCommandClient : ILighterCommandClient
{
    private readonly SignerClient _signer;
    private readonly ILighterRealtimeState _state;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WsLighterCommandClient> _logger;
    private bool _disposed;

    /// <summary>
    /// Lighter API error code for invalid nonce.
    /// </summary>
    private const int InvalidNonceErrorCode = 21104;

    /// <summary>
    /// Maximum number of retry attempts for nonce errors.
    /// </summary>
    private const int MaxNonceRetries = 3;

    /// <summary>
    /// Delay in milliseconds between nonce retry attempts.
    /// </summary>
    private const int NonceRetryDelayMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsLighterCommandClient"/> class.
    /// </summary>
    /// <param name="signer">Signer client for signing transactions.</param>
    /// <param name="state">Real-time state for market data access.</param>
    /// <param name="httpClient">HTTP client for transaction submission.</param>
    /// <param name="logger">Logger instance.</param>
    public WsLighterCommandClient(
        SignerClient signer,
        ILighterRealtimeState state,
        HttpClient httpClient,
        ILogger<WsLighterCommandClient> logger)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RespSendTx> CreateOrderAsync(
        CreateOrderRequest request,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.CreateOrderAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign order: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.CreateOrder,
                    txInfo!,
                    priceProtection,
                    cancellationToken);
            },
            "CreateOrder",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> CreateMarketOrderAsync(
        MarketOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        // Get order book from WebSocket state
        var orderBook = _state.GetOrderBook(request.MarketIndex);
        if (orderBook == null)
        {
            throw new LighterApiException(
                $"Order book not available for market {request.MarketIndex}. " +
                "Ensure the market is subscribed via WebSocket.");
        }

        // For sell orders: use best bid price
        // For buy orders: use best ask price
        decimal idealPrice;
        if (request.IsAsk)
        {
            if (orderBook.Bids.Count == 0)
                throw new LighterApiException("No bids available in order book for SELL order");
            idealPrice = orderBook.BestBidPrice;
        }
        else
        {
            if (orderBook.Asks.Count == 0)
                throw new LighterApiException("No asks available in order book for BUY order");
            idealPrice = orderBook.BestAskPrice;
        }

        // Calculate acceptable execution price with slippage
        var slippageMultiplier = request.IsAsk ? (1 - request.MaxSlippage) : (1 + request.MaxSlippage);
        var executionPrice = (long)Math.Round(idealPrice * slippageMultiplier * 100); // Scale to cents

        var orderRequest = new CreateOrderRequest
        {
            MarketIndex = request.MarketIndex,
            ClientOrderIndex = request.ClientOrderIndex,
            BaseAmount = request.BaseAmount,
            Price = executionPrice,
            IsAsk = request.IsAsk,
            OrderType = OrderType.Market,
            TimeInForce = TimeInForce.ImmediateOrCancel,
            ReduceOnly = request.ReduceOnly,
            TriggerPrice = OrderConstants.NilTriggerPrice,
            OrderExpiry = OrderConstants.DefaultIocExpiry
        };

        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.CreateOrderAsync(orderRequest);
                if (error != null)
                    throw new LighterApiException($"Failed to sign market order: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.CreateOrder,
                    txInfo!,
                    priceProtection: true,
                    cancellationToken);
            },
            "CreateMarketOrder",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> CreateGroupedOrdersAsync(
        CreateGroupedOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.CreateGroupedOrdersAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign grouped orders: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.CreateGroupedOrders,
                    txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CreateGroupedOrders",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> CancelOrderAsync(
        int marketId,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var result = await _signer.CancelOrderAsync(marketId, orderId);
                if (result.error != null)
                    throw new LighterApiException($"Failed to sign order cancellation: {result.error}");

                return await SendTransactionAsync(
                    TransactionTypes.CancelOrder,
                    result.txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CancelOrder",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> CancelAllOrdersAsync(
        int marketId,
        long cancelTimestampMs = 0,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "CancelAllOrdersAsync called (marketId={MarketId} is ignored - cancels ALL orders across all markets)",
            marketId);

        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var result = await _signer.CancelAllOrdersAsync(timeInForce: 0, cancelTimestampMs: cancelTimestampMs);
                if (result.error != null)
                    throw new LighterApiException($"Failed to sign cancel all orders: {result.error}");

                return await SendTransactionAsync(
                    TransactionTypes.CancelAllOrders,
                    result.txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CancelAllOrders",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> ModifyOrderAsync(
        ModifyOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.ModifyOrderAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign order modification: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.ModifyOrder,
                    txInfo!,
                    cancellationToken: cancellationToken);
            },
            "ModifyOrder",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespSendTx> UpdateLeverageAsync(
        UpdateLeverageRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.UpdateLeverageAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign leverage update: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.UpdateLeverage,
                    txInfo!,
                    cancellationToken: cancellationToken);
            },
            "UpdateLeverage",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> SyncNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        // In pure WebSocket mode, we don't have REST access to sync nonce
        // The SignerClient tracks nonce locally after initialization
        throw new NotSupportedException(
            "SyncNonceAsync is not supported in WebSocket-only mode. " +
            "Nonce is tracked locally by SignerClient after initialization.");
    }

    /// <inheritdoc />
    public async Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600)
    {
        return await _signer.CreateAuthTokenAsync(validitySeconds);
    }

    /// <inheritdoc />
    public async Task<SignedOrderResult> SignOrderAsync(CreateOrderRequest request)
    {
        var (txInfo, error) = await _signer.CreateOrderAsync(request);
        if (error != null)
            return new SignedOrderResult(0, string.Empty, error);

        return new SignedOrderResult(TransactionTypes.CreateOrder, txInfo!, null);
    }

    /// <inheritdoc />
    public async Task<BatchOrderResult> SubmitOrderBatchAsync(
        SignedOrderResult[] signedOrders,
        CancellationToken cancellationToken = default)
    {
        if (signedOrders == null || signedOrders.Length == 0)
        {
            return new BatchOrderResult
            {
                IsSuccess = false,
                OrdersSubmitted = 0,
                TxHashes = [],
                ErrorMessage = "No orders to submit"
            };
        }

        var signingErrors = signedOrders.Where(o => o.Error != null).ToList();
        if (signingErrors.Count > 0)
        {
            return new BatchOrderResult
            {
                IsSuccess = false,
                OrdersSubmitted = 0,
                TxHashes = [],
                ErrorMessage = $"Signing failed for {signingErrors.Count} orders: {signingErrors[0].Error}"
            };
        }

        var txTypes = signedOrders.Select(o => o.TxType).ToArray();
        var txInfos = signedOrders.Select(o => o.TxInfo).ToArray();

        try
        {
            var response = await SendTransactionBatchAsync(txTypes, txInfos, cancellationToken);

            return new BatchOrderResult
            {
                IsSuccess = response.IsSuccess,
                OrdersSubmitted = response.IsSuccess ? signedOrders.Length : 0,
                TxHashes = response.TxHashArray,
                ErrorMessage = response.IsSuccess ? null : response.Message,
                Code = response.Code
            };
        }
        catch (LighterApiException ex)
        {
            return new BatchOrderResult
            {
                IsSuccess = false,
                OrdersSubmitted = 0,
                TxHashes = [],
                ErrorMessage = ex.Message,
                Code = ex.Code ?? 0
            };
        }
    }

    /// <inheritdoc />
    public async Task<BatchOrderResult> CreateOrderBatchAsync(
        CreateOrderRequest[] requests,
        CancellationToken cancellationToken = default)
    {
        if (requests == null || requests.Length == 0)
        {
            return new BatchOrderResult
            {
                IsSuccess = false,
                OrdersSubmitted = 0,
                TxHashes = [],
                ErrorMessage = "No orders to create"
            };
        }

        // Sign all orders sequentially (nonces must be sequential)
        var signedOrders = new SignedOrderResult[requests.Length];
        for (var i = 0; i < requests.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signedOrders[i] = await SignOrderAsync(requests[i]);

            if (signedOrders[i].Error != null)
            {
                _logger.LogWarning("Batch signing failed at order {Index}: {Error}", i, signedOrders[i].Error);
                return new BatchOrderResult
                {
                    IsSuccess = false,
                    OrdersSubmitted = 0,
                    TxHashes = [],
                    ErrorMessage = $"Failed to sign order {i}: {signedOrders[i].Error}"
                };
            }
        }

        _logger.LogDebug("Signed {Count} orders, submitting batch...", requests.Length);
        return await SubmitOrderBatchAsync(signedOrders, cancellationToken);
    }

    /// <summary>
    /// Submits a single signed transaction to the Lighter API.
    /// </summary>
    private async Task<RespSendTx> SendTransactionAsync(
        int txType,
        string txInfo,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txInfo))
            throw new ArgumentException("Transaction info cannot be null or empty.", nameof(txInfo));

        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent(txType.ToString()), "tx_type");
        formContent.Add(new StringContent(txInfo), "tx_info");
        if (priceProtection.HasValue)
            formContent.Add(new StringContent(priceProtection.Value.ToString().ToLowerInvariant()), "price_protection");

        _logger.LogDebug("POST sendTx: tx_type={TxType}", txType);

        var response = await _httpClient.PostAsync("sendTx", formContent, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response);

        var result = await response.Content.ReadFromJsonAsync<RespSendTx>(LighterJsonOptions.Default, cancellationToken)
            ?? throw new LighterApiException("Failed to deserialize response");

        if (!result.IsSuccess)
            throw new LighterApiException(result.Message ?? "Transaction submission failed", result.Code);

        return result;
    }

    /// <summary>
    /// Submits multiple signed transactions in a batch.
    /// </summary>
    private async Task<RespSendTxBatch> SendTransactionBatchAsync(
        int[] txTypes,
        string[] txInfos,
        CancellationToken cancellationToken = default)
    {
        if (txTypes == null || txTypes.Length == 0)
            throw new ArgumentException("Transaction types cannot be null or empty.", nameof(txTypes));

        if (txInfos == null || txInfos.Length == 0)
            throw new ArgumentException("Transaction infos cannot be null or empty.", nameof(txInfos));

        if (txTypes.Length != txInfos.Length)
            throw new ArgumentException("Transaction types and infos arrays must have the same length.");

        var txTypesJson = JsonSerializer.Serialize(txTypes);
        var txInfosJson = JsonSerializer.Serialize(txInfos);

        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent(txTypesJson), "tx_types");
        formContent.Add(new StringContent(txInfosJson), "tx_infos");

        _logger.LogDebug("POST sendTxBatch: {Count} transactions", txTypes.Length);

        var response = await _httpClient.PostAsync("sendTxBatch", formContent, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response);

        var result = await response.Content.ReadFromJsonAsync<RespSendTxBatch>(LighterJsonOptions.Default, cancellationToken)
            ?? throw new LighterApiException("Failed to deserialize batch response");

        if (!result.IsSuccess)
            throw new LighterApiException(result.Message ?? "Batch transaction submission failed", result.Code);

        return result;
    }

    /// <summary>
    /// Executes an operation with automatic retry on nonce errors.
    /// </summary>
    private async Task<RespSendTx> ExecuteWithNonceRetryAsync(
        Func<Task<RespSendTx>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        int retryCount = 0;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (LighterApiException ex) when (ex.Code == InvalidNonceErrorCode && retryCount < MaxNonceRetries)
            {
                retryCount++;
                _logger.LogWarning(
                    "Nonce error on {Operation} (code {Code}), incrementing local nonce (attempt {Attempt}/{Max})",
                    operationName, ex.Code, retryCount, MaxNonceRetries);

                // In WS-only mode, we can't sync from server, so just wait and retry
                // The SignerClient's nonce will be incremented on the next signing attempt
                await Task.Delay(NonceRetryDelayMs, cancellationToken);
            }
        }
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new LighterApiException(
                $"API request failed with status {(int)response.StatusCode}: {content}",
                (int)response.StatusCode);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _signer?.Dispose();
        _disposed = true;
    }
}
