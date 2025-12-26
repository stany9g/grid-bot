using System.Threading.Channels;
using GridBot.Extended.Models.WebSocket;

namespace GridBot.Extended;

/// <summary>
/// WebSocket client interface for Extended DEX real-time data streaming.
/// </summary>
public interface IExtendedWebSocketClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the channel reader for order book updates.
    /// </summary>
    ChannelReader<OrderBookUpdateEvent> OrderBookUpdates { get; }

    /// <summary>
    /// Gets the channel reader for account updates.
    /// </summary>
    ChannelReader<AccountUpdateEvent> AccountUpdates { get; }

    /// <summary>
    /// Gets the channel reader for order updates.
    /// </summary>
    ChannelReader<OrderUpdateEvent> OrderUpdates { get; }

    /// <summary>
    /// Gets the channel reader for trade updates.
    /// </summary>
    ChannelReader<TradeUpdateEvent> TradeUpdates { get; }

    /// <summary>
    /// Gets the channel reader for balance updates.
    /// </summary>
    ChannelReader<BalanceUpdateEvent> BalanceUpdates { get; }

    /// <summary>
    /// Gets the channel reader for position updates.
    /// </summary>
    ChannelReader<PositionUpdateEvent> PositionUpdates { get; }

    /// <summary>
    /// Gets the channel reader for connection state changes.
    /// </summary>
    ChannelReader<ConnectionStateEvent> ConnectionStateChanges { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState ConnectionState { get; }

    /// <summary>
    /// Gets whether the WebSocket is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the time since the last message was received.
    /// </summary>
    TimeSpan? TimeSinceLastMessage { get; }

    /// <summary>
    /// Gets the number of disconnections in the last 24 hours.
    /// </summary>
    int DisconnectCount24h { get; }

    /// <summary>
    /// Connects to the WebSocket server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the connection operation.</returns>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnects from the WebSocket server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the disconnection operation.</returns>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribes to order book updates for a market.
    /// </summary>
    /// <param name="market">Market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the subscription operation.</returns>
    Task SubscribeOrderBookAsync(string market, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to private account updates (orders, trades, balances, positions).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the subscription operation.</returns>
    Task SubscribeAccountAsync(CancellationToken ct = default);

    /// <summary>
    /// Unsubscribes from a channel.
    /// </summary>
    /// <param name="channel">Channel to unsubscribe from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the unsubscription operation.</returns>
    Task UnsubscribeAsync(string channel, CancellationToken ct = default);
}
