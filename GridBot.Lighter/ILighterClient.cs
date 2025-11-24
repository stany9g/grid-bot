using GridBot.Lighter.Api;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// Interface for the Lighter protocol client that combines local signing with API operations.
/// Provides high-level convenience methods for common workflows.
/// </summary>
public interface ILighterClient : IDisposable
{
    /// <summary>
    /// Gets the underlying SignerClient for direct access to signing operations.
    /// </summary>
    SignerClient Signer { get; }

    /// <summary>
    /// Gets the underlying API client for direct access to API operations.
    /// </summary>
    LighterApiClient Api { get; }

    #region Order Operations

    /// <summary>
    /// Creates and submits a limit, market, stop-loss, or take-profit order in a single operation.
    /// Signs the order locally and submits it to the API.
    /// </summary>
    /// <param name="request">Order creation request.</param>
    /// <param name="priceProtection">Enable price protection (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CreateOrderAsync(
        CreateOrderRequest request,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and submits grouped orders (OCO, OTO, OTOCO) in a single operation.
    /// Signs the orders locally and submits them to the API.
    /// </summary>
    /// <param name="request">Grouped orders creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CreateGroupedOrdersAsync(
        CreateGroupedOrdersRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a specific order in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CancelOrderAsync(
        int marketId,
        long orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all orders in a market in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="timeInForce">Time in force for the cancellation (default: 0 = immediate).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CancelAllOrdersAsync(
        int marketId,
        long timeInForce = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Modifies an existing order in a single operation.
    /// Signs the modification locally and submits it to the API.
    /// </summary>
    /// <param name="request">Order modification request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> ModifyOrderAsync(
        ModifyOrderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates position leverage in a single operation.
    /// Signs the update locally and submits it to the API.
    /// </summary>
    /// <param name="request">Leverage update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> UpdateLeverageAsync(
        UpdateLeverageRequest request,
        CancellationToken cancellationToken = default);

    #endregion

    #region Account & Market Data Operations

    /// <summary>
    /// Gets account information including positions and balances.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account details.</returns>
    Task<Account> GetAccountAsync(
        long accountIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets account metadata including public key and status.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account metadata.</returns>
    Task<AccountMetadata> GetAccountMetadataAsync(
        long accountIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active orders for an account.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active orders.</returns>
    Task<List<Order>> GetActiveOrdersAsync(
        long accountIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all order book metadata for all markets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of order books.</returns>
    Task<List<OrderBook>> GetOrderBooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed order book data for a specific market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="depth">Maximum number of price levels to return (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book details with bid/ask levels.</returns>
    Task<OrderBookDetail> GetOrderBookDetailsAsync(
        int marketId,
        int? depth = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction by its hash or sequence index.
    /// </summary>
    /// <param name="hashOrIndex">Transaction hash (0x...) or sequence index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction details.</returns>
    Task<Tx> GetTransactionAsync(
        string hashOrIndex,
        CancellationToken cancellationToken = default);

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
    Task<NextNonce> GetNextNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes the local signer nonce with the server.
    /// Call this method if you encounter nonce mismatch errors.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synchronized nonce value.</returns>
    Task<long> SyncNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default);

    #endregion
}
