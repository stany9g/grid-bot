using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

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
