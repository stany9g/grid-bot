using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Account update message from WebSocket (private /account channel).
/// This is the main message type for all account updates.
/// </summary>
public sealed record AccountUpdateMessage : WebSocketMessageBase
{
    /// <summary>
    /// Account address.
    /// </summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// Update type: "order", "trade", "balance", "position".
    /// </summary>
    [JsonPropertyName("updateType")]
    public string? UpdateType { get; init; }

    /// <summary>
    /// Order update data (if updateType == "order").
    /// </summary>
    [JsonPropertyName("order")]
    public OrderUpdateData? Order { get; init; }

    /// <summary>
    /// Trade update data (if updateType == "trade").
    /// </summary>
    [JsonPropertyName("trade")]
    public TradeUpdateData? Trade { get; init; }

    /// <summary>
    /// Balance update data (if updateType == "balance").
    /// </summary>
    [JsonPropertyName("balance")]
    public BalanceUpdateData? Balance { get; init; }

    /// <summary>
    /// Position update data (if updateType == "position").
    /// </summary>
    [JsonPropertyName("position")]
    public PositionUpdateData? Position { get; init; }
}

/// <summary>
/// Order update data within account message.
/// </summary>
public sealed record OrderUpdateData
{
    /// <summary>
    /// Exchange order ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Client order ID.
    /// </summary>
    [JsonPropertyName("clientOrderId")]
    public string? ClientOrderId { get; init; }

    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Order side.
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Order type.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Order price.
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; init; }

    /// <summary>
    /// Original quantity.
    /// </summary>
    [JsonPropertyName("qty")]
    public required string Qty { get; init; }

    /// <summary>
    /// Filled quantity.
    /// </summary>
    [JsonPropertyName("filledQty")]
    public required string FilledQty { get; init; }

    /// <summary>
    /// Order status: "open", "partial", "filled", "cancelled", "rejected".
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Rejection reason (if status == "rejected").
    /// </summary>
    [JsonPropertyName("rejectReason")]
    public string? RejectReason { get; init; }

    /// <summary>
    /// Whether this order was reduce-only.
    /// </summary>
    [JsonPropertyName("reduceOnly")]
    public bool ReduceOnly { get; init; }
}

/// <summary>
/// Trade update data within account message.
/// </summary>
public sealed record TradeUpdateData
{
    /// <summary>
    /// Trade ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Associated order ID.
    /// </summary>
    [JsonPropertyName("orderId")]
    public required string OrderId { get; init; }

    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Trade side.
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Execution price.
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; init; }

    /// <summary>
    /// Trade quantity.
    /// </summary>
    [JsonPropertyName("qty")]
    public required string Qty { get; init; }

    /// <summary>
    /// Fee paid.
    /// </summary>
    [JsonPropertyName("fee")]
    public string? Fee { get; init; }

    /// <summary>
    /// Fee asset.
    /// </summary>
    [JsonPropertyName("feeAsset")]
    public string? FeeAsset { get; init; }

    /// <summary>
    /// Whether this was a maker or taker trade.
    /// </summary>
    [JsonPropertyName("isMaker")]
    public bool IsMaker { get; init; }
}

/// <summary>
/// Balance update data within account message.
/// </summary>
public sealed record BalanceUpdateData
{
    /// <summary>
    /// Asset symbol.
    /// </summary>
    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    /// <summary>
    /// Total balance.
    /// </summary>
    [JsonPropertyName("total")]
    public required string Total { get; init; }

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("available")]
    public required string Available { get; init; }

    /// <summary>
    /// Locked balance.
    /// </summary>
    [JsonPropertyName("locked")]
    public string? Locked { get; init; }
}

/// <summary>
/// Position update data within account message.
/// </summary>
public sealed record PositionUpdateData
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Position size.
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; init; }

    /// <summary>
    /// Position side.
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Average entry price.
    /// </summary>
    [JsonPropertyName("entryPrice")]
    public required string EntryPrice { get; init; }

    /// <summary>
    /// Mark price.
    /// </summary>
    [JsonPropertyName("markPrice")]
    public required string MarkPrice { get; init; }

    /// <summary>
    /// Unrealized PnL.
    /// </summary>
    [JsonPropertyName("unrealizedPnl")]
    public required string UnrealizedPnl { get; init; }

    /// <summary>
    /// Liquidation price.
    /// </summary>
    [JsonPropertyName("liquidationPrice")]
    public string? LiquidationPrice { get; init; }

    /// <summary>
    /// Leverage.
    /// </summary>
    [JsonPropertyName("leverage")]
    public int Leverage { get; init; }
}
