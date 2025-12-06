using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// Interface for Lighter command operations (orders and transactions).
/// Handles signing and submission of write operations.
/// </summary>
public interface ILighterCommandClient : IDisposable
{
    /// <summary>
    /// Creates and submits a limit, market, stop-loss, or take-profit order in a single operation.
    /// Signs the order locally and submits it to the API.
    /// </summary>
    /// <param name="request">Order creation request.</param>
    /// <param name="priceProtection">Enable price protection (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CreateOrderAsync(CreateOrderRequest request, bool? priceProtection = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and submits grouped orders (OCO, OTO, OTOCO) in a single operation.
    /// Signs the orders locally and submits them to the API.
    /// </summary>
    /// <param name="request">Grouped orders creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CreateGroupedOrdersAsync(CreateGroupedOrdersRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a specific order in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CancelOrderAsync(int marketId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all orders in a market in a single operation.
    /// Signs the cancellation locally and submits it to the API.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="timeInForce">Time in force for the cancellation (default: 0 = immediate).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CancelAllOrdersAsync(int marketId, long timeInForce = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Modifies an existing order in a single operation.
    /// Signs the modification locally and submits it to the API.
    /// </summary>
    /// <param name="request">Order modification request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> ModifyOrderAsync(ModifyOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates position leverage in a single operation.
    /// Signs the update locally and submits it to the API.
    /// </summary>
    /// <param name="request">Leverage update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> UpdateLeverageAsync(UpdateLeverageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes the local signer nonce with the server.
    /// Call this method if you encounter nonce mismatch errors.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synchronized nonce value.</returns>
    Task<long> SyncNonceAsync(long accountIndex, int apiKeyIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and submits a market order with automatic slippage protection.
    /// Fetches current market price from order book and calculates acceptable execution price.
    /// </summary>
    /// <param name="request">Market order request with slippage tolerance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    Task<RespSendTx> CreateMarketOrderAsync(MarketOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an authentication token for accessing private API endpoints.
    /// The token is signed using the account's API private key.
    /// </summary>
    /// <param name="validitySeconds">How long the token should be valid (default 600 = 10 minutes).</param>
    /// <returns>Tuple containing (authToken, error). If error is not null, token creation failed.</returns>
    Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600);
}
