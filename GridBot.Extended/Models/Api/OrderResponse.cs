using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Order information from Extended API.
/// </summary>
public sealed record OrderResponse
{
    /// <summary>
    /// Exchange-assigned order ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Client-provided order ID.
    /// </summary>
    [JsonPropertyName("clientOrderId")]
    public string? ClientOrderId { get; init; }

    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Order side: "BUY" or "SELL".
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Order type: "limit", "market", etc.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Order price (for limit orders).
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; init; }

    /// <summary>
    /// Original order quantity.
    /// </summary>
    [JsonPropertyName("qty")]
    public required string Qty { get; init; }

    /// <summary>
    /// Filled quantity.
    /// </summary>
    [JsonPropertyName("filledQty")]
    public required string FilledQty { get; init; }

    /// <summary>
    /// Remaining quantity.
    /// </summary>
    [JsonPropertyName("remainingQty")]
    public string? RemainingQty { get; init; }

    /// <summary>
    /// Order status: "open", "partial", "filled", "cancelled", "rejected".
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Time in force: "GTT", "IOC", "FOK", "POST_ONLY".
    /// </summary>
    [JsonPropertyName("timeInForce")]
    public string? TimeInForce { get; init; }

    /// <summary>
    /// Whether this is a reduce-only order.
    /// </summary>
    [JsonPropertyName("reduceOnly")]
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Order creation timestamp (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; init; }

    /// <summary>
    /// Order expiry timestamp (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("expiryEpochMillis")]
    public long? ExpiryEpochMillis { get; init; }

    /// <summary>
    /// Average fill price.
    /// </summary>
    [JsonPropertyName("avgFillPrice")]
    public string? AvgFillPrice { get; init; }

    /// <summary>
    /// Fee rate applied.
    /// </summary>
    [JsonPropertyName("fee")]
    public string? Fee { get; init; }
}

/// <summary>
/// Response for order creation (the data inside ApiResponse wrapper).
/// </summary>
public sealed record CreateOrderResponse
{
    /// <summary>
    /// Exchange-assigned order ID (numeric).
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// External order ID (the hash/signature based ID).
    /// </summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }
}
