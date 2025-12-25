using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.OrderBook;
using GridBot.Abstractions.Models.Orders;

namespace GridBot.Abstractions.Communication;

/// <summary>
/// Provides real-time market data and account updates via WebSocket or streaming connection.
/// </summary>
public interface IRealtimeDataProvider
{
    /// <summary>
    /// Gets the current order book snapshot for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The order book snapshot, or null if not available.</returns>
    OrderBookSnapshot? GetOrderBook(string marketId);

    /// <summary>
    /// Gets the current account information.
    /// </summary>
    /// <returns>The account info, or null if not available.</returns>
    AccountInfo? GetAccount();

    /// <summary>
    /// Gets the current active orders for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>List of active orders.</returns>
    IReadOnlyList<OrderInfo> GetOrders(string marketId);

    /// <summary>
    /// Gets the current price for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The current price, or null if not available.</returns>
    decimal? GetCurrentPrice(string marketId);

    /// <summary>
    /// Gets the current position for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The position info, or null if no position exists.</returns>
    PositionInfo? GetPosition(string marketId);

    /// <summary>
    /// Gets whether market data is ready for a specific market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>True if market data is available and fresh.</returns>
    bool IsMarketDataReady(string marketId);

    /// <summary>
    /// Gets whether the real-time connection is established.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Subscribes to real-time updates for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the subscription operation.</returns>
    Task SubscribeMarketAsync(string marketId, CancellationToken ct = default);

    /// <summary>
    /// Waits until market data is available for a specific market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="timeout">Maximum time to wait. Null uses a default timeout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when market data is ready.</returns>
    Task WaitForMarketDataAsync(string marketId, TimeSpan? timeout = null, CancellationToken ct = default);
}
