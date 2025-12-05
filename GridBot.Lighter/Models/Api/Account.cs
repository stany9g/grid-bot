using System.Globalization;
using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Wrapper response for account API calls.
/// </summary>
public sealed class AccountResponse
{
    /// <summary>
    /// Response code (200 = success)
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Total number of accounts returned
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// List of accounts
    /// </summary>
    [JsonPropertyName("accounts")]
    public List<Account> Accounts { get; set; } = new();

    /// <summary>
    /// Whether the response indicates success
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200;
}

/// <summary>
/// Represents an account in the Lighter system.
/// </summary>
public sealed class Account
{
    /// <summary>
    /// Response code
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Account type
    /// </summary>
    [JsonPropertyName("account_type")]
    public int AccountType { get; set; }

    /// <summary>
    /// Account index (same as account_index)
    /// </summary>
    [JsonPropertyName("index")]
    public long Index { get; set; }

    /// <summary>
    /// L1 (Ethereum) address
    /// </summary>
    [JsonPropertyName("l1_address")]
    public string L1Address { get; set; } = string.Empty;

    /// <summary>
    /// Cancel all time
    /// </summary>
    [JsonPropertyName("cancel_all_time")]
    public long CancelAllTime { get; set; }

    /// <summary>
    /// Total order count
    /// </summary>
    [JsonPropertyName("total_order_count")]
    public int TotalOrderCount { get; set; }

    /// <summary>
    /// Total isolated order count
    /// </summary>
    [JsonPropertyName("total_isolated_order_count")]
    public int TotalIsolatedOrderCount { get; set; }

    /// <summary>
    /// Pending order count
    /// </summary>
    [JsonPropertyName("pending_order_count")]
    public int PendingOrderCount { get; set; }

    /// <summary>
    /// Available balance
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; set; } = string.Empty;

    /// <summary>
    /// Account status: 1 = active, 0 = inactive
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Account's collateral amount
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; set; } = string.Empty;

    /// <summary>
    /// Account index
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; set; }

    /// <summary>
    /// Account name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Account description
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether the account can invite
    /// </summary>
    [JsonPropertyName("can_invite")]
    public bool CanInvite { get; set; }

    /// <summary>
    /// Referral points percentage
    /// </summary>
    [JsonPropertyName("referral_points_percentage")]
    public string ReferralPointsPercentage { get; set; } = string.Empty;

    /// <summary>
    /// List of positions
    /// </summary>
    [JsonPropertyName("positions")]
    public List<Position> Positions { get; set; } = new();

    /// <summary>
    /// Total asset value
    /// </summary>
    [JsonPropertyName("total_asset_value")]
    public string TotalAssetValue { get; set; } = string.Empty;

    /// <summary>
    /// Cross asset value
    /// </summary>
    [JsonPropertyName("cross_asset_value")]
    public string CrossAssetValue { get; set; } = string.Empty;

    /// <summary>
    /// Shares
    /// </summary>
    [JsonPropertyName("shares")]
    public List<object> Shares { get; set; } = new();

    /// <summary>
    /// Whether the account is active
    /// </summary>
    [JsonIgnore]
    public bool IsActive => Status == 1;
}

/// <summary>
/// Represents a position in a market.
/// </summary>
public sealed class Position
{
    /// <summary>
    /// Market ID
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Symbol (e.g., "BTC")
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Initial margin fraction
    /// </summary>
    [JsonPropertyName("initial_margin_fraction")]
    public string InitialMarginFraction { get; set; } = string.Empty;

    /// <summary>
    /// Open order count for this market
    /// </summary>
    [JsonPropertyName("open_order_count")]
    public int OpenOrderCount { get; set; }

    /// <summary>
    /// Pending order count
    /// </summary>
    [JsonPropertyName("pending_order_count")]
    public int PendingOrderCount { get; set; }

    /// <summary>
    /// Position tied order count
    /// </summary>
    [JsonPropertyName("position_tied_order_count")]
    public int PositionTiedOrderCount { get; set; }

    /// <summary>
    /// Position direction: 1 = Long, -1 = Short, 0 = No position
    /// </summary>
    [JsonPropertyName("sign")]
    public int Sign { get; set; }

    /// <summary>
    /// Position size (amount)
    /// </summary>
    [JsonPropertyName("position")]
    public string Positionn { get; set; } = "0";

    /// <summary>
    /// Average entry price
    /// </summary>
    [JsonPropertyName("avg_entry_price")]
    public string AvgEntryPrice { get; set; } = "0";

    /// <summary>
    /// Position value in USD
    /// </summary>
    [JsonPropertyName("position_value")]
    public string PositionValue { get; set; } = "0";

    /// <summary>
    /// Unrealized profit/loss
    /// </summary>
    [JsonPropertyName("unrealized_pnl")]
    public string UnrealizedPnl { get; set; } = "0";

    /// <summary>
    /// Realized profit/loss
    /// </summary>
    [JsonPropertyName("realized_pnl")]
    public string RealizedPnl { get; set; } = "0";

    /// <summary>
    /// Liquidation price
    /// </summary>
    [JsonPropertyName("liquidation_price")]
    public string LiquidationPrice { get; set; } = "0";

    /// <summary>
    /// Margin mode: 0 = cross, 1 = isolated
    /// </summary>
    [JsonPropertyName("margin_mode")]
    public int MarginMode { get; set; }

    /// <summary>
    /// Allocated margin
    /// </summary>
    [JsonPropertyName("allocated_margin")]
    public string AllocatedMargin { get; set; } = "0";

    /// <summary>
    /// Position side as enum for easier handling
    /// </summary>
    [JsonIgnore]
    public PositionSide Side => Sign switch
    {
        1 => PositionSide.Long,
        -1 => PositionSide.Short,
        _ => PositionSide.None
    };

    /// <summary>
    /// Whether the position is open
    /// </summary>
    [JsonIgnore]
    public bool IsOpen => Sign != 0 && decimal.TryParse(Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) && size != 0;

    /// <summary>
    /// Market identifier for compatibility (uses Symbol)
    /// </summary>
    [JsonIgnore]
    public string Market => Symbol;

    /// <summary>
    /// Position size alias for compatibility
    /// </summary>
    [JsonIgnore]
    public string PositionSize => Positionn;

    /// <summary>
    /// Entry price alias for compatibility
    /// </summary>
    [JsonIgnore]
    public string EntryPrice => AvgEntryPrice;
}

/// <summary>
/// Position side enumeration
/// </summary>
public enum PositionSide
{
    None = 0,
    Long = 1,
    Short = -1
}
