using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// Client for Lighter command operations (orders and transactions).
/// Handles signing and submission of write operations.
/// </summary>
public sealed class LighterCommandClient : ILighterCommandClient
{
    private readonly SignerClient _signer;
    private readonly ILighterQueryClient _queryClient;
    private readonly HttpClient _writeHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Lighter API error code for invalid nonce.
    /// </summary>
    private const int InvalidNonceErrorCode = 21104;

    /// <summary>
    /// Maximum number of retry attempts for nonce errors.
    /// </summary>
    private const int MaxNonceRetries = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterCommandClient"/> class.
    /// </summary>
    /// <param name="queryClient">The query client for fetching nonce data.</param>
    /// <param name="writeHttpClient">The HTTP client for write operations.</param>
    /// <param name="signer">The signer client for signing transactions.</param>
    public LighterCommandClient(ILighterQueryClient queryClient, HttpClient writeHttpClient, SignerClient signer)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _writeHttpClient = writeHttpClient ?? throw new ArgumentNullException(nameof(writeHttpClient));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _jsonOptions = CreateJsonOptions();
    }


    /// <summary>
    /// Creates and submits a limit, market, stop-loss, or take-profit order in a single operation.
    /// Signs the order locally and submits it to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="request">Order creation request.</param>
    /// <param name="priceProtection">Enable price protection (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> CreateOrderAsync(
        CreateOrderRequest request,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the order locally
                var (txInfo, error) = await _signer.CreateOrderAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign order: {error}");

                // Submit to API
                return await SendTransactionAsync(
                    TransactionTypes.CreateOrder,
                    txInfo!,
                    priceProtection,
                    cancellationToken);
            },
            "CreateOrder",
            cancellationToken);
    }

    /// <summary>
    /// Creates and submits a market order with automatic slippage protection.
    /// Fetches current market price from order book and calculates acceptable execution price.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="request">Market order request with slippage tolerance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails, order book is empty, or the API returns an error.</exception>
    public async Task<RespSendTx> CreateMarketOrderAsync(
        MarketOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        // Fetch order book orders to get current market price (bids and asks)
        var orderBook = await _queryClient.GetOrderBookOrdersAsync(request.MarketIndex, limit: 1, cancellationToken);

        // For sell orders: use best bid price (what buyers will pay)
        // For buy orders: use best ask price (what sellers want)
        long idealPrice;
        if (request.IsAsk)
        {
            if (orderBook.Bids == null || orderBook.Bids.Count == 0)
                throw new LighterApiException("No bids available in order book for SELL order");

            idealPrice = ParseScaledPrice(orderBook.Bids[0].Price);
        }
        else
        {
            if (orderBook.Asks == null || orderBook.Asks.Count == 0)
                throw new LighterApiException("No asks available in order book for BUY order");

            idealPrice = ParseScaledPrice(orderBook.Asks[0].Price);
        }

        // Calculate acceptable execution price with slippage
        // For sell: idealPrice * (1 - slippage) = willing to accept less
        // For buy: idealPrice * (1 + slippage) = willing to pay more
        var slippageMultiplier = request.IsAsk ? (1 - request.MaxSlippage) : (1 + request.MaxSlippage);
        var executionPrice = (long)Math.Round(idealPrice * slippageMultiplier);

        // Create the order request with calculated price
        // IOC/Market orders MUST use expiry=0 (DefaultIocExpiry)
        // The signer rejects -1 for IOC orders since they execute immediately
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

        // Sign and submit the order with retry on nonce error
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                var (txInfo, error) = await _signer.CreateOrderAsync(orderRequest);
                if (error != null)
                    throw new LighterApiException($"Failed to sign market order: {error}");

                return await SendTransactionAsync(
                    TransactionTypes.CreateOrder,
                    txInfo!,
                    priceProtection: true, // Enable price protection for market orders
                    cancellationToken);
            },
            "CreateMarketOrder",
            cancellationToken);
    }

    /// <summary>
    /// Parses a price string (e.g., "97000.50") to a scaled long value.
    /// Removes the decimal point to get the scaled integer representation.
    /// </summary>
    private static long ParseScaledPrice(string priceString)
    {
        if (string.IsNullOrWhiteSpace(priceString))
            throw new ArgumentException("Price string cannot be null or empty", nameof(priceString));

        // Remove decimal point to get scaled value (e.g., "97000.50" -> 9700050)
        var cleanPrice = priceString.Replace(".", "");
        if (!long.TryParse(cleanPrice, out var scaledPrice))
            throw new LighterApiException($"Failed to parse price: {priceString}");

        return scaledPrice;
    }

    /// <summary>
    /// Creates and submits grouped orders (OCO, OTO, OTOCO) in a single operation.
    /// Signs the orders locally and submits them to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="request">Grouped orders creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> CreateGroupedOrdersAsync(
        CreateGroupedOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the grouped orders locally
                var (txInfo, error) = await _signer.CreateGroupedOrdersAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign grouped orders: {error}");

                // Submit to API
                return await SendTransactionAsync(
                    TransactionTypes.CreateGroupedOrders,
                    txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CreateGroupedOrders",
            cancellationToken);
    }

    /// <summary>
    /// Cancels a specific order in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> CancelOrderAsync(
        int marketId,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the cancellation locally
                var result = await _signer.CancelOrderAsync(marketId, orderId);
                if (result.error != null)
                    throw new LighterApiException($"Failed to sign order cancellation: {result.error}");

                // Submit to API
                return await SendTransactionAsync(
                    TransactionTypes.CancelOrder,
                    result.txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CancelOrder",
            cancellationToken);
    }

    /// <summary>
    /// Cancels all orders in a market in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="timeInForce">Time in force for the cancellation (default: 0 = immediate).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> CancelAllOrdersAsync(
        int marketId,
        long timeInForce = 0,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the cancellation locally
                var result = await _signer.CancelAllOrdersAsync(marketId, timeInForce);
                if (result.error != null)
                    throw new LighterApiException($"Failed to sign cancel all orders: {result.error}");

                // Submit to API
                return await SendTransactionAsync(
                    TransactionTypes.CancelAllOrders,
                    result.txInfo!,
                    cancellationToken: cancellationToken);
            },
            "CancelAllOrders",
            cancellationToken);
    }

    /// <summary>
    /// Modifies an existing order in a single operation.
    /// Signs the modification locally and submits it to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="request">Order modification request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> ModifyOrderAsync(
        ModifyOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the modification locally
                var (txInfo, error) = await _signer.ModifyOrderAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign order modification: {error}");

                // Submit to API
                return await SendTransactionAsync(
                    TransactionTypes.ModifyOrder,
                    txInfo!,
                    cancellationToken: cancellationToken);
            },
            "ModifyOrder",
            cancellationToken);
    }

    /// <summary>
    /// Updates position leverage in a single operation.
    /// Signs the update locally and submits it to the API.
    /// Automatically retries with nonce resync if a nonce mismatch error occurs.
    /// </summary>
    /// <param name="request">Leverage update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> UpdateLeverageAsync(
        UpdateLeverageRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithNonceRetryAsync(
            async () =>
            {
                // Sign the update locally
                var (txInfo, error) = await _signer.UpdateLeverageAsync(request);
                if (error != null)
                    throw new LighterApiException($"Failed to sign leverage update: {error}");

        // Submit to API
        return await SendTransactionAsync(
            TransactionTypes.UpdateLeverage,
            txInfo!,
            cancellationToken: cancellationToken);
    },
            "UpdateLeverage",
            cancellationToken);
    }



    /// <summary>
    /// Synchronizes the local signer nonce with the server.
    /// Call this method if you encounter nonce mismatch errors.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synchronized nonce value.</returns>
    public async Task<long> SyncNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        var response = await _queryClient.GetNextNonceAsync(accountIndex, apiKeyIndex, cancellationToken);
        // SetNonce sets _currentNonce, but GetNextNonce() increments BEFORE returning.
        // So if server says "next nonce is 3", we set _currentNonce = 3 - 1 = 2,
        // then when GetNextNonce() does ++_currentNonce, it returns 3 (the correct value).
        Console.WriteLine($"[SyncNonce] Server returned NextNonce={response.Nonce}, setting internal nonce to {response.Nonce - 1}");
        _signer.SetNonce(response.Nonce - 1);
        return response.Nonce;
    }

    /// <summary>
    /// Executes an operation with automatic nonce resync on error 21104.
    /// If the operation fails with "invalid nonce", resyncs from server and retries.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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
                Console.WriteLine($"[LighterCommandClient] Nonce error on {operationName} (code {ex.Code}), " +
                                  $"resyncing nonce (attempt {retryCount}/{MaxNonceRetries})");

                // Resync nonce from server
                await SyncNonceAsync(_signer.AccountIndex, _signer.ApiKeyIndex, cancellationToken);

                Console.WriteLine($"[LighterCommandClient] Nonce resynced, retrying {operationName}");
            }
        }
    }

    /// <summary>
    /// Generates a new API key pair for signing transactions.
    /// </summary>
    /// <returns>Tuple of (private key, public key, error). Check error for null before using the keys.</returns>
    public static Task<(string? privateKey, string? publicKey, string? error)> GenerateApiKeyAsync(string? seed = null)
        => SignerClient.GenerateApiKeyAsync(seed);



    /// <summary>
    /// Submits a single signed transaction to the Lighter API.
    /// </summary>
    /// <param name="txType">Transaction type (see <see cref="TransactionTypes"/>).</param>
    /// <param name="txInfo">Signed transaction info JSON string from SignerClient.</param>
    /// <param name="priceProtection">Enable price protection (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    internal async Task<RespSendTx> SendTransactionAsync(
        int txType,
        string txInfo,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txInfo))
            throw new ArgumentException("Transaction info cannot be null or empty.", nameof(txInfo));

        // API expects multipart/form-data with tx_info as a string
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent(txType.ToString()), "tx_type");
        formContent.Add(new StringContent(txInfo), "tx_info");
        if (priceProtection.HasValue)
            formContent.Add(new StringContent(priceProtection.Value.ToString().ToLowerInvariant()), "price_protection");

        Console.WriteLine($"[LighterCommandClient] POST sendTx (form-data): tx_type={txType}, tx_info={txInfo}");

        var response = await _writeHttpClient.PostAsync("sendTx", formContent, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response);
        var content = await response.Content.ReadAsStringAsync();
        var result = await response.Content.ReadFromJsonAsync<RespSendTx>(_jsonOptions, cancellationToken)
            ?? throw new LighterApiException("Failed to deserialize response");

        if (!result.IsSuccess)
            throw new LighterApiException(result.Message ?? "Transaction submission failed", result.Code);

        return result;
    }

    /// <summary>
    /// Submits multiple signed transactions in a batch to the Lighter API.
    /// </summary>
    /// <param name="txTypes">Array of transaction types.</param>
    /// <param name="txInfos">Array of signed transaction info JSON strings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hashes and predicted execution times.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    internal async Task<RespSendTxBatch> SendTransactionBatchAsync(
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

        // Use PascalCase - JsonNamingPolicy.SnakeCaseLower will convert to snake_case
        var request = new
        {
            TxTypes = string.Join(",", txTypes),
            TxInfos = string.Join(",", txInfos)
        };

        var response = await PostAsync<RespSendTxBatch>("sendTxBatch", request, cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Batch transaction submission failed", response.Code);

        return response;
    }



    private async Task<T> PostAsync<T>(string endpoint, object request, CancellationToken cancellationToken)
    {
        try
        {
            // Serialize manually for full control
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            Console.WriteLine($"[LighterCommandClient] POST {endpoint}: {json}");

            // Use StringContent instead of PostAsJsonAsync for explicit control
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _writeHttpClient.PostAsync(endpoint, content, cancellationToken);
            await EnsureSuccessStatusCodeAsync(response);

            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new LighterApiException("Failed to deserialize response");
        }
        catch (HttpRequestException ex)
        {
            throw new LighterApiException($"HTTP request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LighterApiException("Request timed out", ex);
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            // Use relaxed escaping to avoid \u0022 for quotes (use \" instead)
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }



    /// <summary>
    /// Creates an authentication token for accessing private API endpoints.
    /// The token is signed using the account's API private key.
    /// </summary>
    /// <param name="validitySeconds">How long the token should be valid (default 600 = 10 minutes).</param>
    /// <returns>Tuple containing (authToken, error). If error is not null, token creation failed.</returns>
    public async Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600)
    {
        return await _signer.CreateAuthTokenAsync(validitySeconds);
    }

    /// <summary>
    /// Disposes the client and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _signer?.Dispose();
        _writeHttpClient?.Dispose();
        _disposed = true;
    }

}
