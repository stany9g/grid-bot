using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Request to create an order on Extended.
/// Based on Python SDK's NewOrderModel in x10/perpetual/orders.py
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
    /// Order type: "LIMIT" (uppercase required).
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
    public required string Price { get; init; }

    /// <summary>
    /// Fee rate (decimal, e.g., 0.0006 = 0.06%).
    /// </summary>
    [JsonPropertyName("fee")]
    public required string Fee { get; init; }

    /// <summary>
    /// Order expiry timestamp (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("expiryEpochMillis")]
    public required long ExpiryEpochMillis { get; init; }

    /// <summary>
    /// Time in force: "GTT", "IOC".
    /// Note: FOK is NOT supported for new orders per Python SDK.
    /// </summary>
    [JsonPropertyName("timeInForce")]
    public required string TimeInForce { get; init; }

    /// <summary>
    /// Whether this is a reduce-only order.
    /// </summary>
    [JsonPropertyName("reduceOnly")]
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Whether this is a post-only order.
    /// </summary>
    [JsonPropertyName("postOnly")]
    public bool PostOnly { get; init; }

    /// <summary>
    /// Nonce for this transaction (must be at order root, not in settlement).
    /// </summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    /// <summary>
    /// Self-trade protection level: "ACCOUNT", "CLIENT", or "DISABLED".
    /// </summary>
    [JsonPropertyName("selfTradeProtectionLevel")]
    public required string SelfTradeProtectionLevel { get; init; }

    /// <summary>
    /// Settlement object containing Stark signature components.
    /// </summary>
    [JsonPropertyName("settlement")]
    public required SettlementObject Settlement { get; init; }
}

/// <summary>
/// Settlement object containing Stark signature for order authentication.
/// Based on Python SDK's StarkSettlementModel in x10/perpetual/orders.py
/// </summary>
public sealed record SettlementObject
{
    /// <summary>
    /// Stark public key (hex string with 0x prefix).
    /// </summary>
    [JsonPropertyName("starkKey")]
    public required string StarkKey { get; init; }

    /// <summary>
    /// Collateral position (vault ID from account info's l2Vault).
    /// </summary>
    [JsonPropertyName("collateralPosition")]
    public required string CollateralPosition { get; init; }

    /// <summary>
    /// Signature object containing R and S components.
    /// </summary>
    [JsonPropertyName("signature")]
    public required SignatureObject Signature { get; init; }
}

/// <summary>
/// Stark signature components.
/// Based on Python SDK's SettlementSignatureModel in x10/utils/model.py
/// </summary>
public sealed record SignatureObject
{
    /// <summary>
    /// Signature R component (hex string with 0x prefix).
    /// </summary>
    [JsonPropertyName("r")]
    public required string R { get; init; }

    /// <summary>
    /// Signature S component (hex string with 0x prefix).
    /// </summary>
    [JsonPropertyName("s")]
    public required string S { get; init; }
}
