using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Base record for all WebSocket messages.
/// </summary>
public abstract record WebSocketMessage
{
    /// <summary>
    /// Message type identifier.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Channel this message relates to.
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; init; }
}

/// <summary>
/// Subscription request message (client -> server).
/// </summary>
public sealed record SubscribeMessage : WebSocketMessage
{
    /// <summary>
    /// Authentication token for private channels.
    /// </summary>
    [JsonPropertyName("auth")]
    public string? Auth { get; init; }
}

/// <summary>
/// Unsubscription request message (client -> server).
/// </summary>
public sealed record UnsubscribeMessage : WebSocketMessage;

/// <summary>
/// Pong response message (client -> server).
/// </summary>
public sealed record PongMessage : WebSocketMessage;

/// <summary>
/// Ping message from server.
/// </summary>
public sealed record PingMessage : WebSocketMessage;

/// <summary>
/// Subscription confirmation from server.
/// </summary>
public sealed record SubscribedMessage : WebSocketMessage
{
    /// <summary>
    /// Message offset for resumption.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }
}

/// <summary>
/// Error message from server.
/// </summary>
public sealed record ErrorMessage : WebSocketMessage
{
    /// <summary>
    /// Error code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

#region Order Book Channel

/// <summary>
/// Order book update message.
/// Channel: order_book/{MARKET_INDEX}
/// </summary>
public sealed record OrderBookMessage : WebSocketMessage
{
    /// <summary>
    /// Order book data.
    /// </summary>
    [JsonPropertyName("order_book")]
    public OrderBookData? OrderBook { get; init; }

    /// <summary>
    /// Message offset.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }
}

/// <summary>
/// Order book data structure.
/// </summary>
public sealed record OrderBookData
{
    /// <summary>
    /// Response code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Ask orders (sell side).
    /// </summary>
    [JsonPropertyName("asks")]
    public List<OrderBookLevel> Asks { get; init; } = [];

    /// <summary>
    /// Bid orders (buy side).
    /// </summary>
    [JsonPropertyName("bids")]
    public List<OrderBookLevel> Bids { get; init; } = [];

    /// <summary>
    /// Message offset.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }

    /// <summary>
    /// Nonce value.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; init; }

    /// <summary>
    /// Timestamp in milliseconds.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Order book price level.
/// </summary>
public sealed record OrderBookLevel
{
    /// <summary>
    /// Price as string for precision.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// Size as string for precision.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";
}

#endregion

#region Account All Channel

/// <summary>
/// Account update message from account_all channel.
/// Channel: account_all/{ACCOUNT_ID}
/// </summary>
public sealed record AccountAllMessage : WebSocketMessage
{
    /// <summary>
    /// Account identifier.
    /// </summary>
    [JsonPropertyName("account")]
    public long Account { get; init; }

    /// <summary>
    /// Positions keyed by market ID.
    /// </summary>
    [JsonPropertyName("positions")]
    public Dictionary<string, PositionData>? Positions { get; init; }

    /// <summary>
    /// Trades keyed by market ID.
    /// </summary>
    [JsonPropertyName("trades")]
    public Dictionary<string, List<TradeData>>? Trades { get; init; }

    /// <summary>
    /// Pool shares.
    /// </summary>
    [JsonPropertyName("shares")]
    public List<PoolSharesData>? Shares { get; init; }

    /// <summary>
    /// Funding histories keyed by market ID.
    /// </summary>
    [JsonPropertyName("funding_histories")]
    public Dictionary<string, List<FundingHistoryData>>? FundingHistories { get; init; }

    /// <summary>
    /// Asset balances keyed by asset ID.
    /// Note: This field may be present in actual API responses but is not in official docs.
    /// </summary>
    [JsonPropertyName("assets")]
    public Dictionary<string, AssetBalance>? Assets { get; init; }

    /// <summary>
    /// Daily trade count.
    /// </summary>
    [JsonPropertyName("daily_trades_count")]
    public int DailyTradesCount { get; init; }

    /// <summary>
    /// Daily volume.
    /// </summary>
    [JsonPropertyName("daily_volume")]
    public decimal DailyVolume { get; init; }

    /// <summary>
    /// Weekly trade count.
    /// </summary>
    [JsonPropertyName("weekly_trades_count")]
    public int WeeklyTradesCount { get; init; }

    /// <summary>
    /// Weekly volume.
    /// </summary>
    [JsonPropertyName("weekly_volume")]
    public decimal WeeklyVolume { get; init; }

    /// <summary>
    /// Monthly trade count.
    /// </summary>
    [JsonPropertyName("monthly_trades_count")]
    public int MonthlyTradesCount { get; init; }

    /// <summary>
    /// Monthly volume.
    /// </summary>
    [JsonPropertyName("monthly_volume")]
    public decimal MonthlyVolume { get; init; }

    /// <summary>
    /// Total trade count.
    /// </summary>
    [JsonPropertyName("total_trades_count")]
    public int TotalTradesCount { get; init; }

    /// <summary>
    /// Total volume.
    /// </summary>
    [JsonPropertyName("total_volume")]
    public decimal TotalVolume { get; init; }
}

/// <summary>
/// Asset balance data.
/// </summary>
public sealed record AssetBalance
{
    /// <summary>
    /// Asset symbol (e.g., "ETH", "USDC").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Asset identifier.
    /// </summary>
    [JsonPropertyName("asset_id")]
    public int AssetId { get; init; }

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("balance")]
    public string Balance { get; init; } = "0";

    /// <summary>
    /// Locked balance (in orders).
    /// </summary>
    [JsonPropertyName("locked_balance")]
    public string LockedBalance { get; init; } = "0";
}

/// <summary>
/// Position data structure.
/// </summary>
public sealed record PositionData
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; init; }

    /// <summary>
    /// Symbol (e.g., "BTC-USD").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Initial margin fraction.
    /// </summary>
    [JsonPropertyName("initial_margin_fraction")]
    public string InitialMarginFraction { get; init; } = "0";

    /// <summary>
    /// Open order count for this market.
    /// </summary>
    [JsonPropertyName("open_order_count")]
    public int OpenOrderCount { get; init; }

    /// <summary>
    /// Pending order count.
    /// </summary>
    [JsonPropertyName("pending_order_count")]
    public int PendingOrderCount { get; init; }

    /// <summary>
    /// Position tied order count.
    /// </summary>
    [JsonPropertyName("position_tied_order_count")]
    public int PositionTiedOrderCount { get; init; }

    /// <summary>
    /// Position sign: 1 = Long, -1 = Short, 0 = None.
    /// </summary>
    [JsonPropertyName("sign")]
    public int Sign { get; init; }

    /// <summary>
    /// Position size as string for precision.
    /// </summary>
    [JsonPropertyName("position")]
    public string Position { get; init; } = "0";

    /// <summary>
    /// Average entry price.
    /// </summary>
    [JsonPropertyName("avg_entry_price")]
    public string AvgEntryPrice { get; init; } = "0";

    /// <summary>
    /// Position value.
    /// </summary>
    [JsonPropertyName("position_value")]
    public string PositionValue { get; init; } = "0";

    /// <summary>
    /// Unrealized profit/loss.
    /// </summary>
    [JsonPropertyName("unrealized_pnl")]
    public string UnrealizedPnl { get; init; } = "0";

    /// <summary>
    /// Realized profit/loss.
    /// </summary>
    [JsonPropertyName("realized_pnl")]
    public string RealizedPnl { get; init; } = "0";

    /// <summary>
    /// Liquidation price.
    /// </summary>
    [JsonPropertyName("liquidation_price")]
    public string LiquidationPrice { get; init; } = string.Empty;

    /// <summary>
    /// Total funding paid out.
    /// </summary>
    [JsonPropertyName("total_funding_paid_out")]
    public string TotalFundingPaidOut { get; init; } = "0";

    /// <summary>
    /// Margin mode: 0 = cross, 1 = isolated.
    /// </summary>
    [JsonPropertyName("margin_mode")]
    public int MarginMode { get; init; }

    /// <summary>
    /// Allocated margin for isolated positions.
    /// </summary>
    [JsonPropertyName("allocated_margin")]
    public string AllocatedMargin { get; init; } = "0";
}

/// <summary>
/// Trade data structure.
/// </summary>
public sealed record TradeData
{
    /// <summary>
    /// Trade identifier.
    /// </summary>
    [JsonPropertyName("trade_id")]
    public long TradeId { get; init; }

    /// <summary>
    /// Transaction hash.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string TxHash { get; init; } = string.Empty;

    /// <summary>
    /// Trade type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; init; }

    /// <summary>
    /// Trade size.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    /// <summary>
    /// Trade price.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// USD amount.
    /// </summary>
    [JsonPropertyName("usd_amount")]
    public string UsdAmount { get; init; } = "0";

    /// <summary>
    /// Ask order ID.
    /// </summary>
    [JsonPropertyName("ask_id")]
    public long AskId { get; init; }

    /// <summary>
    /// Bid order ID.
    /// </summary>
    [JsonPropertyName("bid_id")]
    public long BidId { get; init; }

    /// <summary>
    /// Ask account ID.
    /// </summary>
    [JsonPropertyName("ask_account_id")]
    public long AskAccountId { get; init; }

    /// <summary>
    /// Bid account ID.
    /// </summary>
    [JsonPropertyName("bid_account_id")]
    public long BidAccountId { get; init; }

    /// <summary>
    /// Whether the maker was the ask side.
    /// </summary>
    [JsonPropertyName("is_maker_ask")]
    public bool IsMakerAsk { get; init; }

    /// <summary>
    /// Block height.
    /// </summary>
    [JsonPropertyName("block_height")]
    public long BlockHeight { get; init; }

    /// <summary>
    /// Timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    /// <summary>
    /// Taker fee (omitted when zero).
    /// </summary>
    [JsonPropertyName("taker_fee")]
    public int TakerFee { get; init; }

    /// <summary>
    /// Maker fee (omitted when zero).
    /// </summary>
    [JsonPropertyName("maker_fee")]
    public int MakerFee { get; init; }
}

/// <summary>
/// Pool shares data.
/// </summary>
public sealed record PoolSharesData
{
    /// <summary>
    /// Public pool index.
    /// </summary>
    [JsonPropertyName("public_pool_index")]
    public int PublicPoolIndex { get; init; }

    /// <summary>
    /// Shares amount.
    /// </summary>
    [JsonPropertyName("shares_amount")]
    public int SharesAmount { get; init; }

    /// <summary>
    /// Entry USDC value.
    /// </summary>
    [JsonPropertyName("entry_usdc")]
    public string EntryUsdc { get; init; } = "0";
}

/// <summary>
/// Funding history entry.
/// </summary>
public sealed record FundingHistoryData
{
    /// <summary>
    /// Timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    /// <summary>
    /// Market ID.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; init; }

    /// <summary>
    /// Funding ID.
    /// </summary>
    [JsonPropertyName("funding_id")]
    public int FundingId { get; init; }

    /// <summary>
    /// Funding change amount.
    /// </summary>
    [JsonPropertyName("change")]
    public string Change { get; init; } = "0";

    /// <summary>
    /// Funding rate.
    /// </summary>
    [JsonPropertyName("rate")]
    public string Rate { get; init; } = "0";

    /// <summary>
    /// Position size at time of funding.
    /// </summary>
    [JsonPropertyName("position_size")]
    public string PositionSize { get; init; } = "0";

    /// <summary>
    /// Position side (long/short).
    /// </summary>
    [JsonPropertyName("position_side")]
    public string PositionSide { get; init; } = string.Empty;
}

#endregion

#region Account All Orders Channel

/// <summary>
/// Orders update message.
/// Channel: account_all_orders/{ACCOUNT_ID}
/// </summary>
public sealed record OrdersMessage : WebSocketMessage
{
    /// <summary>
    /// Orders grouped by market ID (string keys).
    /// </summary>
    [JsonPropertyName("orders")]
    public Dictionary<string, List<OrderData>> Orders { get; init; } = [];
}

/// <summary>
/// Order data structure from WebSocket.
/// </summary>
public sealed record OrderData
{
    /// <summary>
    /// Order index/identifier.
    /// </summary>
    [JsonPropertyName("order_index")]
    public long OrderIndex { get; init; }

    /// <summary>
    /// Client order index.
    /// </summary>
    [JsonPropertyName("client_order_index")]
    public long ClientOrderIndex { get; init; }

    /// <summary>
    /// Order ID as string (same as order_index).
    /// </summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;

    /// <summary>
    /// Client order ID (same as client_order_index).
    /// </summary>
    [JsonPropertyName("client_order_id")]
    public string ClientOrderId { get; init; } = string.Empty;

    /// <summary>
    /// Market index.
    /// </summary>
    [JsonPropertyName("market_index")]
    public int MarketIndex { get; init; }

    /// <summary>
    /// Owner account index.
    /// </summary>
    [JsonPropertyName("owner_account_index")]
    public long OwnerAccountIndex { get; init; }

    /// <summary>
    /// Initial base amount (order size).
    /// </summary>
    [JsonPropertyName("initial_base_amount")]
    public string InitialBaseAmount { get; init; } = "0";

    /// <summary>
    /// Order price.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// Nonce used for this order.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; init; }

    /// <summary>
    /// Remaining base amount.
    /// </summary>
    [JsonPropertyName("remaining_base_amount")]
    public string RemainingBaseAmount { get; init; } = "0";

    /// <summary>
    /// Whether this is an ask (sell) order.
    /// </summary>
    [JsonPropertyName("is_ask")]
    public bool IsAsk { get; init; }

    /// <summary>
    /// Base size (integer).
    /// </summary>
    [JsonPropertyName("base_size")]
    public long BaseSize { get; init; }

    /// <summary>
    /// Base price (integer).
    /// </summary>
    [JsonPropertyName("base_price")]
    public long BasePrice { get; init; }

    /// <summary>
    /// Filled base amount.
    /// </summary>
    [JsonPropertyName("filled_base_amount")]
    public string FilledBaseAmount { get; init; } = "0";

    /// <summary>
    /// Filled quote amount.
    /// </summary>
    [JsonPropertyName("filled_quote_amount")]
    public string FilledQuoteAmount { get; init; } = "0";

    /// <summary>
    /// Order side (may be empty, use IsAsk instead).
    /// </summary>
    [JsonPropertyName("side")]
    public string Side { get; init; } = string.Empty;

    /// <summary>
    /// Order type (limit, market, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Time in force setting.
    /// </summary>
    [JsonPropertyName("time_in_force")]
    public string TimeInForce { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is a reduce-only order.
    /// </summary>
    [JsonPropertyName("reduce_only")]
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Trigger price for conditional orders.
    /// </summary>
    [JsonPropertyName("trigger_price")]
    public string TriggerPrice { get; init; } = "0";

    /// <summary>
    /// Order expiry timestamp.
    /// </summary>
    [JsonPropertyName("order_expiry")]
    public long OrderExpiry { get; init; }

    /// <summary>
    /// Order status (open, filled, cancelled, etc.).
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Trigger status for conditional orders.
    /// </summary>
    [JsonPropertyName("trigger_status")]
    public string TriggerStatus { get; init; } = string.Empty;

    /// <summary>
    /// Trigger time.
    /// </summary>
    [JsonPropertyName("trigger_time")]
    public long TriggerTime { get; init; }

    /// <summary>
    /// Parent order index.
    /// </summary>
    [JsonPropertyName("parent_order_index")]
    public long ParentOrderIndex { get; init; }

    /// <summary>
    /// Parent order ID.
    /// </summary>
    [JsonPropertyName("parent_order_id")]
    public string ParentOrderId { get; init; } = "0";

    /// <summary>
    /// First order ID to trigger.
    /// </summary>
    [JsonPropertyName("to_trigger_order_id_0")]
    public string ToTriggerOrderId0 { get; init; } = "0";

    /// <summary>
    /// Second order ID to trigger.
    /// </summary>
    [JsonPropertyName("to_trigger_order_id_1")]
    public string ToTriggerOrderId1 { get; init; } = "0";

    /// <summary>
    /// Order ID to cancel.
    /// </summary>
    [JsonPropertyName("to_cancel_order_id_0")]
    public string ToCancelOrderId0 { get; init; } = "0";

    /// <summary>
    /// Block height when order was created.
    /// </summary>
    [JsonPropertyName("block_height")]
    public long BlockHeight { get; init; }

    /// <summary>
    /// Timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    /// <summary>
    /// Order creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    /// <summary>
    /// Order last update timestamp.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; init; }
}

#endregion

#region Market Stats Channel

/// <summary>
/// Market stats update message.
/// Channel: market_stats/{MARKET_INDEX}
/// </summary>
public sealed record MarketStatsMessage : WebSocketMessage
{
    /// <summary>
    /// Market statistics data.
    /// </summary>
    [JsonPropertyName("market_stats")]
    public MarketStatsData? MarketStats { get; init; }
}

/// <summary>
/// Market statistics data structure.
/// </summary>
public sealed record MarketStatsData
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; init; }

    /// <summary>
    /// Index price.
    /// </summary>
    [JsonPropertyName("index_price")]
    public string IndexPrice { get; init; } = "0";

    /// <summary>
    /// Mark price.
    /// </summary>
    [JsonPropertyName("mark_price")]
    public string MarkPrice { get; init; } = "0";

    /// <summary>
    /// Open interest.
    /// </summary>
    [JsonPropertyName("open_interest")]
    public string OpenInterest { get; init; } = "0";

    /// <summary>
    /// Last trade price.
    /// </summary>
    [JsonPropertyName("last_trade_price")]
    public string LastTradePrice { get; init; } = "0";

    /// <summary>
    /// Current funding rate.
    /// </summary>
    [JsonPropertyName("current_funding_rate")]
    public string CurrentFundingRate { get; init; } = "0";

    /// <summary>
    /// Funding rate.
    /// </summary>
    [JsonPropertyName("funding_rate")]
    public string FundingRate { get; init; } = "0";

    /// <summary>
    /// Funding timestamp.
    /// </summary>
    [JsonPropertyName("funding_timestamp")]
    public long FundingTimestamp { get; init; }

    /// <summary>
    /// Daily base token volume.
    /// </summary>
    [JsonPropertyName("daily_base_token_volume")]
    public decimal DailyBaseTokenVolume { get; init; }

    /// <summary>
    /// Daily quote token volume.
    /// </summary>
    [JsonPropertyName("daily_quote_token_volume")]
    public decimal DailyQuoteTokenVolume { get; init; }

    /// <summary>
    /// Daily price low.
    /// </summary>
    [JsonPropertyName("daily_price_low")]
    public decimal DailyPriceLow { get; init; }

    /// <summary>
    /// Daily price high.
    /// </summary>
    [JsonPropertyName("daily_price_high")]
    public decimal DailyPriceHigh { get; init; }

    /// <summary>
    /// Daily price change percentage.
    /// </summary>
    [JsonPropertyName("daily_price_change")]
    public decimal DailyPriceChange { get; init; }
}

#endregion

#region User Stats Channel

/// <summary>
/// User stats message.
/// Channel: user_stats/{ACCOUNT_ID}
/// </summary>
public sealed record UserStatsMessage : WebSocketMessage
{
    /// <summary>
    /// User statistics data.
    /// </summary>
    [JsonPropertyName("stats")]
    public UserStatsData? Stats { get; init; }
}

/// <summary>
/// User statistics data structure.
/// </summary>
public sealed record UserStatsData
{
    /// <summary>
    /// Collateral amount.
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    /// <summary>
    /// Portfolio value.
    /// </summary>
    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    /// <summary>
    /// Leverage level.
    /// </summary>
    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    /// <summary>
    /// Margin usage percentage.
    /// </summary>
    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    /// <summary>
    /// Buying power.
    /// </summary>
    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";

    /// <summary>
    /// Cross margin statistics.
    /// </summary>
    [JsonPropertyName("cross_stats")]
    public UserStatsDetail? CrossStats { get; init; }

    /// <summary>
    /// Total statistics.
    /// </summary>
    [JsonPropertyName("total_stats")]
    public UserStatsDetail? TotalStats { get; init; }
}

/// <summary>
/// Detailed user statistics for cross/total.
/// </summary>
public sealed record UserStatsDetail
{
    /// <summary>
    /// Collateral amount.
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    /// <summary>
    /// Portfolio value.
    /// </summary>
    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    /// <summary>
    /// Leverage level.
    /// </summary>
    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    /// <summary>
    /// Margin usage percentage.
    /// </summary>
    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    /// <summary>
    /// Buying power.
    /// </summary>
    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";
}

#endregion

#region Notification Channel

/// <summary>
/// Notification message.
/// Channel: notification/{ACCOUNT_ID}
/// </summary>
public sealed record NotificationMessage : WebSocketMessage
{
    /// <summary>
    /// Notification items.
    /// </summary>
    [JsonPropertyName("notifs")]
    public List<NotificationItem>? Notifs { get; init; }
}

/// <summary>
/// Single notification item.
/// </summary>
public sealed record NotificationItem
{
    /// <summary>
    /// Notification ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Update timestamp.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Notification kind (liquidation, deleverage, announcement).
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Account index.
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; init; }

    /// <summary>
    /// Notification content (varies by kind).
    /// </summary>
    [JsonPropertyName("content")]
    public NotificationContent? Content { get; init; }

    /// <summary>
    /// Whether the notification has been acknowledged.
    /// </summary>
    [JsonPropertyName("ack")]
    public bool Ack { get; init; }

    /// <summary>
    /// Acknowledgement timestamp.
    /// </summary>
    [JsonPropertyName("acked_at")]
    public string? AckedAt { get; init; }
}

/// <summary>
/// Notification content (union of all notification types).
/// </summary>
public sealed record NotificationContent
{
    // Common fields
    /// <summary>
    /// Content ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Market index.
    /// </summary>
    [JsonPropertyName("market_index")]
    public int MarketIndex { get; init; }

    /// <summary>
    /// Timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    // Liquidation fields
    /// <summary>
    /// Whether it was an ask (liquidation).
    /// </summary>
    [JsonPropertyName("is_ask")]
    public bool IsAsk { get; init; }

    /// <summary>
    /// USDC amount.
    /// </summary>
    [JsonPropertyName("usdc_amount")]
    public string UsdcAmount { get; init; } = "0";

    /// <summary>
    /// Size.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    /// <summary>
    /// Price.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// Average price.
    /// </summary>
    [JsonPropertyName("avg_price")]
    public string AvgPrice { get; init; } = "0";

    // Deleverage fields
    /// <summary>
    /// Settlement price (deleverage).
    /// </summary>
    [JsonPropertyName("settlement_price")]
    public string SettlementPrice { get; init; } = "0";

    // Announcement fields
    /// <summary>
    /// Announcement title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Announcement content.
    /// </summary>
    [JsonPropertyName("content")]
    public string ContentText { get; init; } = string.Empty;
}

#endregion

#region Trade Channel

/// <summary>
/// Trade update message.
/// Channel: trade/{MARKET_INDEX}
/// </summary>
public sealed record TradeMessage : WebSocketMessage
{
    /// <summary>
    /// Trade data.
    /// </summary>
    [JsonPropertyName("trades")]
    public List<TradeData>? Trades { get; init; }
}

#endregion
