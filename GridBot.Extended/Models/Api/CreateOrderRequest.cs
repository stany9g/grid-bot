using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Request to create an order on Extended.
/// </summary>
public sealed record CreateOrderRequest
{
    /// <summary>
    /// Client-provided order ID for tracking.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Market identifier (e.g., "BTC-USD").
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Order type: "limit", "market".
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Order side: "BUY" or "SELL".
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Order quantity in base asset.
    /// </summary>
    [JsonPropertyName("qty")]
    public required string Qty { get; init; }

    /// <summary>
    /// Order price (required for limit orders).
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; init; }

    /// <summary>
    /// Fee rate (decimal, e.g., 0.0002 = 0.02%).
    /// </summary>
    [JsonPropertyName("fee")]
    public required string Fee { get; init; }

    /// <summary>
    /// Order expiry timestamp (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("expiryEpochMillis")]
    public required long ExpiryEpochMillis { get; init; }

    /// <summary>
    /// Time in force: "GTT", "IOC", "FOK", "POST_ONLY".
    /// </summary>
    [JsonPropertyName("timeInForce")]
    public string? TimeInForce { get; init; }

    /// <summary>
    /// Whether this is a reduce-only order.
    /// </summary>
    [JsonPropertyName("reduceOnly")]
    public bool? ReduceOnly { get; init; }

    /// <summary>
    /// Settlement object containing Stark signature components.
    /// Required for authenticated order submission.
    /// </summary>
    [JsonPropertyName("settlement")]
    public required SettlementObject Settlement { get; init; }
}

/// <summary>
/// Settlement object containing Stark signature for order authentication.
/// </summary>
public sealed record SettlementObject
{
    /// <summary>
    /// Stark public key (hex string).
    /// </summary>
    [JsonPropertyName("starkKey")]
    public required string StarkKey { get; init; }

    /// <summary>
    /// Signature R component (hex string).
    /// </summary>
    [JsonPropertyName("r")]
    public required string R { get; init; }

    /// <summary>
    /// Signature S component (hex string).
    /// </summary>
    [JsonPropertyName("s")]
    public required string S { get; init; }

    /// <summary>
    /// Nonce for this transaction.
    /// </summary>
    [JsonPropertyName("nonce")]
    public required long Nonce { get; init; }

    /// <summary>
    /// Collateral amount (string representation).
    /// </summary>
    [JsonPropertyName("collateral")]
    public string? Collateral { get; init; }

    /// <summary>
    /// Position type.
    /// </summary>
    [JsonPropertyName("positionType")]
    public string? PositionType { get; init; }
}
