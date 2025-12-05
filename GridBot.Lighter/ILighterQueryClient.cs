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
    /// Gets detailed order book metadata for a specific market (fees, margins, stats).
    /// Note: For actual bids/asks, use GetOrderBookOrdersAsync instead.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="depth">Unused parameter (kept for backwards compatibility).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book details with market metadata.</returns>
    Task<OrderBookDetail> GetOrderBookDetailsAsync(int marketId, int? depth = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets order book orders (bids and asks) for a specific market.
    /// This returns the actual order book depth with individual orders.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="limit">Maximum number of orders per side to return (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book orders response with bids and asks.</returns>
    Task<OrderBookOrdersResponse> GetOrderBookOrdersAsync(int marketId, int? limit = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Gets candlestick (OHLCV) data for a market with explicit timestamp range.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="startTimestamp">Start timestamp in milliseconds (Unix epoch).</param>
    /// <param name="endTimestamp">End timestamp in milliseconds (Unix epoch).</param>
    /// <param name="resolution">Candle resolution (1m, 5m, 15m, 1h, 4h, 1d). Default is 1h.</param>
    /// <param name="countBack">Number of candles to return. Default is 20.</param>
    /// <param name="setTimestampToEnd">If true, sets timestamp to end of candle period. Default is false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of candlesticks ordered by timestamp ascending.</returns>
    Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        long startTimestamp,
        long endTimestamp,
        string resolution = "1h",
        int countBack = 20,
        bool setTimestampToEnd = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets candlestick (OHLCV) data for a market. Automatically calculates timestamps based on resolution and countBack.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="resolution">Candle resolution (1m, 5m, 15m, 1h, 4h, 1d). Default is 1h.</param>
    /// <param name="countBack">Number of candles to return. Default is 20.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of candlesticks ordered by timestamp ascending.</returns>
    Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        string resolution = "1h",
        int countBack = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current funding rates across exchanges for all markets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of funding rates.</returns>
    Task<List<FundingRate>> GetFundingRatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent trades for a market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="limit">Maximum number of trades to return. Default is 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent trades ordered by timestamp descending.</returns>
    Task<List<Trade>> GetRecentTradesAsync(
        int marketId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
