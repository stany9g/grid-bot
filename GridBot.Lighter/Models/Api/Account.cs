using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents an account in the Lighter system.
/// </summary>
public sealed class Account
{
    /// <summary>
    /// Account index (unique identifier).
    /// </summary>
    [JsonPropertyName("index")]
    public long Index { get; set; }

    /// <summary>
    /// Layer 1 address associated with this account.
    /// </summary>
    [JsonPropertyName("l1_address")]
    public string L1Address { get; set; } = string.Empty;

    /// <summary>
    /// Account type (e.g., "normal", "maker").
    /// </summary>
    [JsonPropertyName("account_type")]
    public string AccountType { get; set; } = string.Empty;

    /// <summary>
    /// Account status (e.g., "active", "inactive").
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Total collateral in the account.
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; set; } = "0";

    /// <summary>
    /// Available balance for trading.
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; set; } = "0";

    /// <summary>
    /// Total number of orders placed by this account.
    /// </summary>
    [JsonPropertyName("total_order_count")]
    public long TotalOrderCount { get; set; }

    /// <summary>
    /// Number of pending orders.
    /// </summary>
    [JsonPropertyName("pending_order_count")]
    public long PendingOrderCount { get; set; }

    /// <summary>
    /// Timestamp of the last cancel-all operation (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("cancel_all_time")]
    public long? CancelAllTime { get; set; }

    /// <summary>
    /// List of positions held by this account.
    /// </summary>
    [JsonPropertyName("positions")]
    public List<Position>? Positions { get; set; }
}

/// <summary>
/// Represents a position in a market.
/// </summary>
public sealed class Position
{
    /// <summary>
    /// Market ID for this position.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Position size (positive for long, negative for short).
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    /// <summary>
    /// Average entry price for this position.
    /// </summary>
    [JsonPropertyName("entry_price")]
    public string EntryPrice { get; set; } = "0";

    /// <summary>
    /// Current leverage for this position.
    /// </summary>
    [JsonPropertyName("leverage")]
    public string Leverage { get; set; } = "1";

    /// <summary>
    /// Margin mode (e.g., "cross", "isolated").
    /// </summary>
    [JsonPropertyName("margin_mode")]
    public string MarginMode { get; set; } = string.Empty;

    /// <summary>
    /// Unrealized PnL for this position.
    /// </summary>
    [JsonPropertyName("unrealized_pnl")]
    public string UnrealizedPnl { get; set; } = "0";

    /// <summary>
    /// Realized PnL for this position.
    /// </summary>
    [JsonPropertyName("realized_pnl")]
    public string RealizedPnl { get; set; } = "0";

    /// <summary>
    /// Margin allocated to this position.
    /// </summary>
    [JsonPropertyName("margin")]
    public string Margin { get; set; } = "0";
}
