using System.Net.Http.Json;
using System.Text.Json;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// WebSocket-integrated command client for Lighter operations.
/// Uses ILighterRealtimeState for market data and WebSocket for transaction submission.
/// Optionally uses HTTP client for nonce synchronization when WebSocket nonce retries fail.
/// </summary>
public sealed class WsLighterCommandClient : ILighterCommandClient
{
    private readonly SignerClient _signer;
    private readonly ILighterRealtimeState _state;
    private readonly ILighterWebSocketClient _wsClient;
    private readonly ILogger<WsLighterCommandClient> _logger;
    private readonly HttpClient? _httpClient;
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
    /// Maximum age in milliseconds for order book data to be considered fresh for market orders.
    /// </summary>
    private const int MaxOrderBookAgeMs = 2000;

    /// <summary>
    /// Delay in milliseconds between nonce retry attempts.
    /// </summary>
    private const int NonceRetryDelayMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsLighterCommandClient"/> class.
    /// </summary>
    /// <param name="signer">Signer client for signing transactions.</param>
    /// <param name="state">Real-time state for market data access.</param>
    /// <param name="wsClient">WebSocket client for transaction submission.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">Optional HTTP client for nonce synchronization. If not provided, SyncNonceAsync will throw NotSupportedException.</param>
    public WsLighterCommandClient(
        SignerClient signer,
        ILighterRealtimeState state,
        ILighterWebSocketClient wsClient,
        ILogger<WsLighterCommandClient> logger,
        HttpClient? httpClient = null)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient;
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

        // Validate order book freshness for market orders
        var ageMs = (DateTimeOffset.UtcNow - orderBook.LastUpdate).TotalMilliseconds;
        if (ageMs > MaxOrderBookAgeMs)
        {
            throw new LighterApiException(
                $"Order book data is stale ({ageMs:F0}ms old, max {MaxOrderBookAgeMs}ms). " +
                "Cannot execute market order safely. Wait for fresh data or use limit order.");
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
    public async Task<long> SyncNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        if (_httpClient == null)
        {
            throw new NotSupportedException(
                "SyncNonceAsync requires HTTP client. Configure HttpClient in DI registration.");
        }

        var url = $"nextNonce?account_index={accountIndex}&api_key_index={apiKeyIndex}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new LighterApiException(
                    $"Failed to sync nonce: {(int)response.StatusCode} - {errorContent}",
                    (int)response.StatusCode);
            }

            var nonceResponse = await response.Content.ReadFromJsonAsync<NextNonce>(
                LighterJsonOptions.Default, cancellationToken);

            if (nonceResponse == null || !nonceResponse.IsSuccess)
            {
                throw new LighterApiException(
                    nonceResponse?.Message ?? "Failed to parse nonce response",
                    nonceResponse?.Code ?? 0);
            }

            _signer.SetNonce(nonceResponse.Nonce);

            _logger.LogInformation(
                "Synced nonce for account {AccountIndex}, API key {ApiKeyIndex}: {Nonce}",
                accountIndex, apiKeyIndex, nonceResponse.Nonce);

            return nonceResponse.Nonce;
        }
        catch (HttpRequestException ex)
        {
            throw new LighterApiException($"HTTP request failed during nonce sync: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new LighterApiException($"Failed to parse nonce response: {ex.Message}", ex);
        }
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

            var txHashes = response.TxHashArray;
            var partialExecution = response.IsSuccess && txHashes.Length < signedOrders.Length;

            if (partialExecution)
            {
                _logger.LogError(
                    "CRITICAL: Batch partial execution detected! Submitted {Submitted}, Executed {Executed}",
                    signedOrders.Length, txHashes.Length);
            }

            return new BatchOrderResult
            {
                IsSuccess = response.IsSuccess && !partialExecution,
                OrdersSubmitted = txHashes.Length, // Actual count, not assumed
                TxHashes = txHashes,
                ErrorMessage = partialExecution
                    ? $"Partial execution: {txHashes.Length}/{signedOrders.Length} orders succeeded"
                    : (response.IsSuccess ? null : response.Message),
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
    /// Submits a single signed transaction via WebSocket.
    /// </summary>
    private async Task<RespSendTx> SendTransactionAsync(
        int txType,
        string txInfo,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txInfo))
            throw new ArgumentException("Transaction info cannot be null or empty.", nameof(txInfo));

        _logger.LogDebug("WS sendTx: tx_type={TxType}", txType);

        var wsResponse = await _wsClient.SendTransactionAsync(txType, txInfo, priceProtection, cancellationToken);

        if (!wsResponse.IsSuccess)
            throw new LighterApiException(wsResponse.Message ?? "Transaction submission failed", wsResponse.Code);

        return new RespSendTx
        {
            Code = wsResponse.Code,
            Message = wsResponse.Message,
            TxHash = wsResponse.TxHash,
            PredictedExecutionTimeMs = wsResponse.PredictedExecutionTimeMs
        };
    }

    /// <summary>
    /// Submits multiple signed transactions in a batch via WebSocket.
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

        _logger.LogDebug("WS sendTxBatch: {Count} transactions", txTypes.Length);

        var wsResponse = await _wsClient.SendTransactionBatchAsync(txTypes, txInfos, cancellationToken);

        if (!wsResponse.IsSuccess)
            throw new LighterApiException(wsResponse.Message ?? "Batch transaction submission failed", wsResponse.Code);

        return new RespSendTxBatch
        {
            Code = wsResponse.Code,
            Message = wsResponse.Message,
            TxHashesRaw = JsonSerializer.SerializeToElement(wsResponse.TxHashes),
            PredictedExecutionTimeMsRaw = JsonSerializer.SerializeToElement(wsResponse.PredictedExecutionTimeMs)
        };
    }

    /// <summary>
    /// Executes an operation with automatic retry on nonce errors.
    /// When HTTP client is available, attempts to sync nonce from server before retrying.
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
                    "Nonce error on {Operation} (code {Code}), attempt {Attempt}/{Max}",
                    operationName, ex.Code, retryCount, MaxNonceRetries);

                // Try to sync nonce from server if HTTP client is available
                if (_httpClient != null)
                {
                    try
                    {
                        await SyncNonceAsync(
                            _signer.AccountIndex,
                            _signer.ApiKeyIndex,
                            cancellationToken);
                        _logger.LogInformation(
                            "Successfully synced nonce from server for {Operation}",
                            operationName);
                    }
                    catch (Exception syncEx)
                    {
                        _logger.LogWarning(
                            syncEx,
                            "Failed to sync nonce from server for {Operation}, will retry with local nonce",
                            operationName);
                    }
                }

                await Task.Delay(NonceRetryDelayMs, cancellationToken);
            }
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
