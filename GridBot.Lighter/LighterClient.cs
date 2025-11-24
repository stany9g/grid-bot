using GridBot.Lighter.Api;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// Unified client for the Lighter protocol that combines local signing with API operations.
/// Provides high-level convenience methods for common workflows.
/// </summary>
public sealed class LighterClient : ILighterClient
{
    private readonly SignerClient _signer;
    private readonly LighterApiClient _api;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying SignerClient for direct access to signing operations.
    /// </summary>
    public SignerClient Signer => _signer;

    /// <summary>
    /// Gets the underlying API client for direct access to API operations.
    /// </summary>
    public LighterApiClient Api => _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterClient"/> class with default settings.
    /// </summary>
    public LighterClient(string baseUrl) : this(new LighterApiClient(baseUrl))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterClient"/> class with a custom API client.
    /// </summary>
    /// <param name="apiClient">The API client to use for REST operations.</param>
    public LighterClient(LighterApiClient apiClient)
    {
        _api = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _signer = new SignerClient();
    }

    #region High-Level Order Operations

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
        return await _api.SendTransactionAsync(
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
        return await _api.SendTransactionAsync(
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
        return await _api.SendTransactionAsync(
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
        return await _api.SendTransactionAsync(
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
        return await _api.SendTransactionAsync(
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
        return await _api.SendTransactionAsync(
            TransactionTypes.UpdateLeverage,
            txInfo!,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Account & Market Data Operations

    /// <summary>
    /// Gets account information including positions and balances.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account details.</returns>
    public Task<Account> GetAccountAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
        => _api.GetAccountAsync(accountIndex, cancellationToken);

    /// <summary>
    /// Gets account metadata including public key and status.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account metadata.</returns>
    public Task<AccountMetadata> GetAccountMetadataAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
        => _api.GetAccountMetadataAsync(accountIndex, cancellationToken);

    /// <summary>
    /// Gets active orders for an account.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active orders.</returns>
    public Task<List<Order>> GetActiveOrdersAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
        => _api.GetActiveOrdersAsync(accountIndex, cancellationToken);

    /// <summary>
    /// Gets all order book metadata for all markets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of order books.</returns>
    public Task<List<OrderBook>> GetOrderBooksAsync(CancellationToken cancellationToken = default)
        => _api.GetOrderBooksAsync(cancellationToken);

    /// <summary>
    /// Gets detailed order book data for a specific market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="depth">Maximum number of price levels to return (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book details with bid/ask levels.</returns>
    public Task<OrderBookDetail> GetOrderBookDetailsAsync(
        int marketId,
        int? depth = null,
        CancellationToken cancellationToken = default)
        => _api.GetOrderBookDetailsAsync(marketId, depth, cancellationToken);

    /// <summary>
    /// Gets a transaction by its hash or sequence index.
    /// </summary>
    /// <param name="hashOrIndex">Transaction hash (0x...) or sequence index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction details.</returns>
    public Task<Tx> GetTransactionAsync(
        string hashOrIndex,
        CancellationToken cancellationToken = default)
        => _api.GetTransactionAsync(hashOrIndex, cancellationToken);

    #endregion

    #region Nonce Management

    /// <summary>
    /// Gets the next nonce for an account from the server.
    /// Useful for nonce recovery and synchronization.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing the next nonce value.</returns>
    public Task<NextNonce> GetNextNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
        => _api.GetNextNonceAsync(accountIndex, apiKeyIndex, cancellationToken);

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
        var response = await _api.GetNextNonceAsync(accountIndex, apiKeyIndex, cancellationToken);
        _signer.SetNonce(response.Nonce);
        return response.Nonce;
    }

    #endregion

    #region Key Management

    /// <summary>
    /// Generates a new API key pair for signing transactions.
    /// </summary>
    /// <returns>Tuple of (private key, public key, error). Check error for null before using the keys.</returns>
    public static Task<(string? privateKey, string? publicKey, string? error)> GenerateApiKeyAsync(string? seed = null)
        => SignerClient.GenerateApiKeyAsync(seed);

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the client and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _signer?.Dispose();
        _api?.Dispose();
        _disposed = true;
    }

    #endregion
}
