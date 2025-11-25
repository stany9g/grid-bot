using System.Net.Http.Json;
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
    }

    /// <summary>
    /// Creates and submits grouped orders (OCO, OTO, OTOCO) in a single operation.
    /// Signs the orders locally and submits them to the API.
    /// </summary>
    /// <param name="request">Grouped orders creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> CreateGroupedOrdersAsync(
        CreateGroupedOrdersRequest request,
        CancellationToken cancellationToken = default)
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
    }

    /// <summary>
    /// Cancels a specific order in a single operation.
    /// Signs the cancellation locally and submits it to the API.
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
        // Sign the cancellation locally
        var result = await _signer.CancelOrderAsync(marketId, orderId);
        if (result.error != null)
            throw new LighterApiException($"Failed to sign order cancellation: {result.error}");

        // Submit to API
        return await SendTransactionAsync(
            TransactionTypes.CancelOrder,
            result.txInfo!,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels all orders in a market in a single operation.
    /// Signs the cancellation locally and submits it to the API.
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
        // Sign the cancellation locally
        var result = await _signer.CancelAllOrdersAsync(marketId, timeInForce);
        if (result.error != null)
            throw new LighterApiException($"Failed to sign cancel all orders: {result.error}");

        // Submit to API
        return await SendTransactionAsync(
            TransactionTypes.CancelAllOrders,
            result.txInfo!,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Modifies an existing order in a single operation.
    /// Signs the modification locally and submits it to the API.
    /// </summary>
    /// <param name="request">Order modification request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> ModifyOrderAsync(
        ModifyOrderRequest request,
        CancellationToken cancellationToken = default)
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
    }

    /// <summary>
    /// Updates position leverage in a single operation.
    /// Signs the update locally and submits it to the API.
    /// </summary>
    /// <param name="request">Leverage update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when signing fails or the API returns an error.</exception>
    public async Task<RespSendTx> UpdateLeverageAsync(
        UpdateLeverageRequest request,
        CancellationToken cancellationToken = default)
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
        _signer.SetNonce(response.Nonce);
        return response.Nonce;
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

        var request = new
        {
            tx_type = txType,
            tx_info = txInfo,
            price_protection = priceProtection
        };

        var response = await PostAsync<RespSendTx>("sendTx", request, cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Transaction submission failed", response.Code);

        return response;
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

        var request = new
        {
            tx_types = string.Join(",", txTypes),
            tx_infos = string.Join(",", txInfos)
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
            var response = await _writeHttpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
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
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
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
