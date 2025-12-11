using GridBot.Lighter.Models.WebSocket;

namespace GridBot.Lighter;

/// <summary>
/// Thread-safe snapshot access to real-time WebSocket data.
/// Provides latest state for trading decisions.
/// </summary>
public interface ILighterRealtimeState : IAsyncDisposable
{
    /// <summary>
    /// Initializes the service by connecting to WebSocket and subscribing to account data.
    /// Must be called before any other operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Gets the latest order book snapshot for a market.
    /// Returns null if no data received yet.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>Order book snapshot or null.</returns>
    OrderBookSnapshot? GetOrderBook(int marketId);

    /// <summary>
    /// Gets the latest account snapshot.
    /// Returns null if no data received yet.
    /// </summary>
    /// <returns>Account snapshot or null.</returns>
    AccountSnapshot? GetAccount();

    /// <summary>
    /// Gets the latest orders for a market.
    /// Returns empty list if no data received yet.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>List of order snapshots.</returns>
    IReadOnlyList<OrderSnapshot> GetOrders(int marketId);

    /// <summary>
    /// Gets the latest market stats.
    /// Returns null if no data received yet.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>Market stats snapshot or null.</returns>
    MarketStatsSnapshot? GetMarketStats(int marketId);

    /// <summary>
    /// Gets the current price for a market.
    /// Tries order book mid price first, then mark price from market stats.
    /// Returns null if no data received yet.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>Current price or null.</returns>
    decimal? GetCurrentPrice(int marketId);

    /// <summary>
    /// Gets the position size for a market.
    /// Returns null if no data received yet.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>Position size or null.</returns>
    decimal? GetPositionSize(int marketId);

    /// <summary>
    /// Whether WebSocket is connected and providing data.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Age of the oldest data snapshot.
    /// Used to determine if we should fall back to REST.
    /// </summary>
    TimeSpan? OldestDataAge { get; }

    /// <summary>
    /// Checks if market data is available for a specific market.
    /// Returns true when we have received at least one price update (order book or market stats).
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>True if price data is available, false otherwise.</returns>
    bool IsMarketDataReady(int marketId);

    /// <summary>
    /// Checks if order book data is available for a specific market.
    /// Returns true when we have received at least one order book update with bids and asks.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <returns>True if order book data is available, false otherwise.</returns>
    bool IsOrderBookReady(int marketId);

    /// <summary>
    /// Waits until market data is available for a specific market.
    /// Use this after subscribing to market data to ensure WebSocket has received initial data.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds if not specified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when market data is available.</returns>
    /// <exception cref="TimeoutException">Thrown if data is not received within the timeout period.</exception>
    Task WaitForMarketDataAsync(int marketId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until order book data is available for a specific market.
    /// Use this when order book data is required (e.g., for order placement).
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds if not specified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when order book data is available.</returns>
    /// <exception cref="TimeoutException">Thrown if data is not received within the timeout period.</exception>
    Task WaitForOrderBookAsync(int marketId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to data updates for a specific market.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeMarketAsync(int marketId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Account snapshot with positions and balances.
/// </summary>
public sealed record AccountSnapshot
{
    /// <summary>
    /// Account identifier.
    /// </summary>
    public required long AccountId { get; init; }

    /// <summary>
    /// Total collateral amount.
    /// </summary>
    public required decimal Collateral { get; init; }

    /// <summary>
    /// Available balance for trading.
    /// </summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>
    /// Total portfolio value.
    /// </summary>
    public required decimal PortfolioValue { get; init; }

    /// <summary>
    /// Positions by market ID.
    /// </summary>
    public required IReadOnlyDictionary<int, PositionSnapshot> Positions { get; init; }

    /// <summary>
    /// When this snapshot was last updated.
    /// </summary>
    public required DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// Market statistics snapshot.
/// </summary>
public sealed record MarketStatsSnapshot
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Index price.
    /// </summary>
    public required decimal IndexPrice { get; init; }

    /// <summary>
    /// Mark price.
    /// </summary>
    public required decimal MarkPrice { get; init; }

    /// <summary>
    /// Current funding rate.
    /// </summary>
    public required decimal FundingRate { get; init; }

    /// <summary>
    /// 24-hour trading volume.
    /// </summary>
    public required decimal Volume24h { get; init; }

    /// <summary>
    /// When this snapshot was last updated.
    /// </summary>
    public required DateTimeOffset LastUpdated { get; init; }
}
