using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// Interface for read-only Lighter REST API operations.
/// Provides methods to query account, market, and transaction data.
/// </summary>
public interface ILighterQueryClient
{
    /// <summary>
    /// Gets account information including positions and balances.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account details.</returns>
    Task<Account> GetAccountAsync(long accountIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets account metadata including public key and status.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account metadata.</returns>
    Task<AccountMetadata> GetAccountMetadataAsync(long accountIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active orders for an account.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active orders.</returns>
    Task<List<Order>> GetActiveOrdersAsync(long accountIndex, CancellationToken cancellationToken = default);

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
    Task<OrderBookDetail> GetOrderBookDetailsAsync(int marketId, int? depth = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction by its hash or sequence index.
    /// </summary>
    /// <param name="hashOrIndex">Transaction hash (0x...) or sequence index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction details.</returns>
    Task<Tx> GetTransactionAsync(string hashOrIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next nonce for an account from the server.
    /// Useful for nonce recovery and synchronization.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing the next nonce value.</returns>
    Task<NextNonce> GetNextNonceAsync(long accountIndex, int apiKeyIndex, CancellationToken cancellationToken = default);
}
