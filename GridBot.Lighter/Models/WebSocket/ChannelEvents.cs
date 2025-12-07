namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Base record for all channel events pushed to consumers.
/// </summary>
public abstract record ChannelEvent
{
    /// <summary>
    /// When this event was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Order book update event.
/// </summary>
public sealed record OrderBookUpdateEvent : ChannelEvent
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Order book snapshot.
    /// </summary>
    public required OrderBookSnapshot Snapshot { get; init; }

    /// <summary>
    /// Message offset for sequencing.
    /// </summary>
    public long Offset { get; init; }
}

/// <summary>
/// Order book snapshot with aggregated levels.
/// </summary>
public sealed record OrderBookSnapshot
{
    /// <summary>
    /// Best bid price.
    /// </summary>
    public required decimal BestBidPrice { get; init; }

    /// <summary>
    /// Best ask price.
    /// </summary>
    public required decimal BestAskPrice { get; init; }

    /// <summary>
    /// Best bid size.
    /// </summary>
    public required decimal BestBidSize { get; init; }

    /// <summary>
    /// Best ask size.
    /// </summary>
    public required decimal BestAskSize { get; init; }

    /// <summary>
    /// Mid price (average of best bid and ask).
    /// </summary>
    public required decimal MidPrice { get; init; }

    /// <summary>
    /// Bid-ask spread.
    /// </summary>
    public required decimal Spread { get; init; }

    /// <summary>
    /// Spread as percentage of mid price.
    /// </summary>
    public required decimal SpreadPercent { get; init; }

    /// <summary>
    /// All bid levels (price, size).
    /// </summary>
    public required IReadOnlyList<(decimal Price, decimal Size)> Bids { get; init; }

    /// <summary>
    /// All ask levels (price, size).
    /// </summary>
    public required IReadOnlyList<(decimal Price, decimal Size)> Asks { get; init; }
}

/// <summary>
/// Account update event.
/// </summary>
public sealed record AccountUpdateEvent : ChannelEvent
{
    /// <summary>
    /// Account identifier.
    /// </summary>
    public required long AccountId { get; init; }

    /// <summary>
    /// Collateral amount.
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
    /// Position snapshots.
    /// </summary>
    public required IReadOnlyList<PositionSnapshot> Positions { get; init; }
}

/// <summary>
/// Position snapshot.
/// </summary>
public sealed record PositionSnapshot
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Position size (positive = long, negative = short).
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Average entry price.
    /// </summary>
    public required decimal AvgEntryPrice { get; init; }

    /// <summary>
    /// Unrealized profit/loss.
    /// </summary>
    public required decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Liquidation price.
    /// </summary>
    public required decimal LiquidationPrice { get; init; }

    /// <summary>
    /// Whether this position uses cross margin.
    /// </summary>
    public required bool IsCross { get; init; }
}

/// <summary>
/// Order update event.
/// </summary>
public sealed record OrderUpdateEvent : ChannelEvent
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Order snapshots.
    /// </summary>
    public required IReadOnlyList<OrderSnapshot> Orders { get; init; }
}

/// <summary>
/// Order snapshot.
/// </summary>
public sealed record OrderSnapshot
{
    /// <summary>
    /// Order index/identifier.
    /// </summary>
    public required long OrderIndex { get; init; }

    /// <summary>
    /// Order price.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Order size.
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Filled size.
    /// </summary>
    public required decimal FilledSize { get; init; }

    /// <summary>
    /// Order status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Whether this is a buy order.
    /// </summary>
    public required bool IsBuy { get; init; }
}

/// <summary>
/// Market stats update event.
/// </summary>
public sealed record MarketStatsUpdateEvent : ChannelEvent
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
    /// Funding rate.
    /// </summary>
    public required decimal FundingRate { get; init; }

    /// <summary>
    /// 24-hour trading volume.
    /// </summary>
    public required decimal Volume24h { get; init; }
}

/// <summary>
/// Connection state change event.
/// </summary>
public sealed record ConnectionStateEvent : ChannelEvent
{
    /// <summary>
    /// New connection state.
    /// </summary>
    public required ConnectionState State { get; init; }

    /// <summary>
    /// Reason for state change if applicable.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Current reconnection attempt number.
    /// </summary>
    public int ReconnectAttempt { get; init; }
}

/// <summary>
/// WebSocket connection state.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Not connected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Attempting to connect.
    /// </summary>
    Connecting,

    /// <summary>
    /// Connected and operational.
    /// </summary>
    Connected,

    /// <summary>
    /// Reconnecting after disconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Connection failed permanently.
    /// </summary>
    Failed
}

/// <summary>
/// Notification event.
/// </summary>
public sealed record NotificationEvent : ChannelEvent
{
    /// <summary>
    /// Notification type (liquidation_warning, order_filled, etc.).
    /// </summary>
    public required string NotificationType { get; init; }

    /// <summary>
    /// Notification message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Related market identifier if applicable.
    /// </summary>
    public int? MarketId { get; init; }
}
