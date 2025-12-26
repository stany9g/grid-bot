using GridBot.Extended.Models.Api;

namespace GridBot.Extended;

/// <summary>
/// HTTP client interface for Extended DEX REST API operations.
/// </summary>
public interface IExtendedHttpClient
{
    // Public endpoints (no authentication required)

    /// <summary>
    /// Gets all available markets.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of market information.</returns>
    Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets market statistics.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Market statistics.</returns>
    Task<MarketStats> GetMarketStatsAsync(string market, CancellationToken ct = default);

    /// <summary>
    /// Gets order book for a market.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="depth">Number of levels per side (default 20).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Order book snapshot.</returns>
    Task<OrderBookResponse> GetOrderBookAsync(string market, int depth = 20, CancellationToken ct = default);

    /// <summary>
    /// Gets candlestick data.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="candleType">Candle type (e.g., "mark", "index").</param>
    /// <param name="interval">Candle interval (e.g., "1h", "4h", "1d").</param>
    /// <param name="limit">Number of candles to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of candles.</returns>
    Task<IReadOnlyList<CandleResponse>> GetCandlesAsync(
        string market,
        string candleType,
        string interval,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Gets funding rate information.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Funding rate information.</returns>
    Task<FundingRateResponse> GetFundingRateAsync(string market, CancellationToken ct = default);

    // Private endpoints (require API key)

    /// <summary>
    /// Gets account information including current nonce.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Account information.</returns>
    Task<AccountInfoResponse> GetAccountInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets account balances.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of balances.</returns>
    Task<IReadOnlyList<BalanceResponse>> GetBalancesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all open positions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of positions.</returns>
    Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all open orders.
    /// </summary>
    /// <param name="market">Optional market filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of orders.</returns>
    Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(string? market = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a new order.
    /// Note: HTTP 200 does NOT mean order is active - wait for WebSocket confirmation.
    /// </summary>
    /// <param name="request">Order creation request with Stark signature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Order creation response.</returns>
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cancels an order by ID.
    /// </summary>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cancellation was accepted.</returns>
    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// Mass cancels orders.
    /// </summary>
    /// <param name="request">Mass cancel request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Mass cancel response.</returns>
    Task<MassCancelResponse> MassCancelOrdersAsync(MassCancelRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets current leverage for a market.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current leverage value.</returns>
    Task<int> GetLeverageAsync(string market, CancellationToken ct = default);

    /// <summary>
    /// Updates leverage for a market.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="leverage">New leverage value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if update was successful.</returns>
    Task<bool> SetLeverageAsync(string market, int leverage, CancellationToken ct = default);
}
