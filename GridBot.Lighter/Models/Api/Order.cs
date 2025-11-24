using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents an order in the Lighter system.
/// </summary>
public sealed class Order
{
    // Identifiers
    /// <summary>
    /// Order ID (unique identifier).
    /// </summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Client-provided order ID.
    /// </summary>
    [JsonPropertyName("client_order_id")]
    public string? ClientOrderId { get; set; }

    /// <summary>
    /// Order index (numeric identifier).
    /// </summary>
    [JsonPropertyName("order_index")]
    public long OrderIndex { get; set; }

    /// <summary>
    /// Client order index.
    /// </summary>
    [JsonPropertyName("client_order_index")]
    public long? ClientOrderIndex { get; set; }

    // Account and Market
    /// <summary>
    /// Account index that owns this order.
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; set; }

    /// <summary>
    /// Market ID where this order is placed.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    // Amounts
    /// <summary>
    /// Initial base amount when the order was created.
    /// </summary>
    [JsonPropertyName("initial_base_amount")]
    public string InitialBaseAmount { get; set; } = "0";

    /// <summary>
    /// Remaining base amount to be filled.
    /// </summary>
    [JsonPropertyName("remaining_base_amount")]
    public string RemainingBaseAmount { get; set; } = "0";

    /// <summary>
    /// Filled base amount.
    /// </summary>
    [JsonPropertyName("filled_base_amount")]
    public string FilledBaseAmount { get; set; } = "0";

    /// <summary>
    /// Total quote amount filled.
    /// </summary>
    [JsonPropertyName("filled_quote_amount")]
    public string FilledQuoteAmount { get; set; } = "0";

    // Order Configuration
    /// <summary>
    /// Order price (limit price for limit orders).
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    /// <summary>
    /// Order type (e.g., "limit", "market", "stop_loss", "take_profit").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Order side: "buy" or "sell".
    /// </summary>
    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// Time in force (e.g., "gtc", "ioc", "fok").
    /// </summary>
    [JsonPropertyName("time_in_force")]
    public string TimeInForce { get; set; } = string.Empty;

    /// <summary>
    /// Trigger price for stop-loss and take-profit orders.
    /// </summary>
    [JsonPropertyName("trigger_price")]
    public string? TriggerPrice { get; set; }

    // Status and Timestamps
    /// <summary>
    /// Order status (e.g., "open", "filled", "cancelled", "expired").
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Trigger status for conditional orders.
    /// </summary>
    [JsonPropertyName("trigger_status")]
    public string? TriggerStatus { get; set; }

    /// <summary>
    /// Order creation timestamp (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// Order expiry timestamp (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("order_expiry")]
    public long? OrderExpiry { get; set; }

    // Relationships (for grouped orders)
    /// <summary>
    /// Parent order ID (for child orders in grouped orders).
    /// </summary>
    [JsonPropertyName("parent_order_id")]
    public string? ParentOrderId { get; set; }

    /// <summary>
    /// First trigger order ID (for OCO/OTOCO orders).
    /// </summary>
    [JsonPropertyName("to_trigger_order_id_0")]
    public string? ToTriggerOrderId0 { get; set; }

    /// <summary>
    /// Second trigger order ID (for OTOCO orders).
    /// </summary>
    [JsonPropertyName("to_trigger_order_id_1")]
    public string? ToTriggerOrderId1 { get; set; }

    /// <summary>
    /// Triggered by order ID (for conditional orders).
    /// </summary>
    [JsonPropertyName("triggered_by_order_id")]
    public string? TriggeredByOrderId { get; set; }

    // Advanced Fields
    /// <summary>
    /// Whether this order is post-only (maker-only).
    /// </summary>
    [JsonPropertyName("post_only")]
    public bool? PostOnly { get; set; }

    /// <summary>
    /// Whether this order reduces position size only.
    /// </summary>
    [JsonPropertyName("reduce_only")]
    public bool? ReduceOnly { get; set; }

    /// <summary>
    /// Fee paid for this order.
    /// </summary>
    [JsonPropertyName("fee")]
    public string? Fee { get; set; }

    /// <summary>
    /// Average fill price for this order.
    /// </summary>
    [JsonPropertyName("average_fill_price")]
    public string? AverageFillPrice { get; set; }

    /// <summary>
    /// Block height when the order was last updated.
    /// </summary>
    [JsonPropertyName("block_height")]
    public long? BlockHeight { get; set; }

    /// <summary>
    /// Transaction hash that created this order.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string? TxHash { get; set; }

    /// <summary>
    /// Nonce used when creating this order.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long? Nonce { get; set; }
}

/// <summary>
/// Response wrapper for active orders query.
/// </summary>
public sealed class ActiveOrdersResponse
{
    /// <summary>
    /// Response code. 0 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// List of active orders.
    /// </summary>
    [JsonPropertyName("orders")]
    public List<Order> Orders { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful (code == 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 0;
}
