using System.Threading.Channels;
using GridBot.Lighter.Models.WebSocket;

namespace GridBot.Lighter;

/// <summary>
/// WebSocket client for real-time Lighter DEX data streaming.
/// Uses System.Threading.Channels for high-performance event distribution.
/// Thread-safe for concurrent access from multiple consumers.
/// </summary>
public interface ILighterWebSocketClient : IAsyncDisposable
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState ConnectionState { get; }

    /// <summary>
    /// Whether the client is connected and subscriptions are active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Channel reader for order book updates.
    /// </summary>
    ChannelReader<OrderBookUpdateEvent> OrderBookUpdates { get; }

    /// <summary>
    /// Channel reader for account updates.
    /// </summary>
    ChannelReader<AccountUpdateEvent> AccountUpdates { get; }

    /// <summary>
    /// Channel reader for order updates.
    /// </summary>
    ChannelReader<OrderUpdateEvent> OrderUpdates { get; }

    /// <summary>
    /// Channel reader for market stats updates.
    /// </summary>
    ChannelReader<MarketStatsUpdateEvent> MarketStatsUpdates { get; }

    /// <summary>
    /// Channel reader for connection state changes.
    /// </summary>
    ChannelReader<ConnectionStateEvent> ConnectionStateChanges { get; }

    /// <summary>
    /// Channel reader for notifications (liquidation warnings, etc).
    /// </summary>
    ChannelReader<NotificationEvent> Notifications { get; }

    /// <summary>
    /// Connects to WebSocket and starts receiving messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to order book updates for a specific market.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeOrderBookAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to account updates (requires auth).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to order updates (requires auth).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to market stats updates.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeMarketStatsAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to notifications (requires auth).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SubscribeNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a channel.
    /// </summary>
    /// <param name="channel">Channel name to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from WebSocket.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
