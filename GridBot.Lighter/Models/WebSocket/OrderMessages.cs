using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

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
