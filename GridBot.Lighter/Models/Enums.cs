namespace GridBot.Lighter.Models;

/// <summary>
/// Defines the type of order to be placed.
/// </summary>
public enum OrderType : byte
{
    /// <summary>
    /// Limit order - executes at specified price or better.
    /// </summary>
    Limit = 0,

    /// <summary>
    /// Market order - executes immediately at current market price.
    /// </summary>
    Market = 1,

    /// <summary>
    /// Stop-loss order - triggers when price reaches stop level.
    /// </summary>
    StopLoss = 2,

    /// <summary>
    /// Stop-loss limit order - becomes limit order when stop price is reached.
    /// </summary>
    StopLossLimit = 3,

    /// <summary>
    /// Take-profit order - triggers when price reaches profit target.
    /// </summary>
    TakeProfit = 4,

    /// <summary>
    /// Take-profit limit order - becomes limit order when target price is reached.
    /// </summary>
    TakeProfitLimit = 5,

    /// <summary>
    /// Time-weighted average price order - executes over time to achieve average price.
    /// </summary>
    TWAP = 6
}

/// <summary>
/// Defines how long an order remains active.
/// </summary>
public enum TimeInForce : byte
{
    /// <summary>
    /// Immediate-or-cancel - execute immediately and cancel any unfilled portion.
    /// </summary>
    ImmediateOrCancel = 0,

    /// <summary>
    /// Good-till-time - remains active until specified expiry timestamp.
    /// </summary>
    GoodTillTime = 1,

    /// <summary>
    /// Post-only - only adds liquidity, never takes (cancels if would match immediately).
    /// </summary>
    PostOnly = 2
}

/// <summary>
/// Defines the margin mode for a position.
/// </summary>
public enum MarginMode : byte
{
    /// <summary>
    /// Cross margin - all account balance is used as margin.
    /// </summary>
    Cross = 0,

    /// <summary>
    /// Isolated margin - only allocated margin is at risk.
    /// </summary>
    Isolated = 1
}

/// <summary>
/// Defines how orders in a group are related.
/// </summary>
public enum GroupingType : byte
{
    /// <summary>
    /// One-triggers-other - when first order fills, second order is placed.
    /// </summary>
    OneTriggerOther = 1,

    /// <summary>
    /// One-cancels-other - when one order fills, the other is cancelled.
    /// </summary>
    OneCancelOther = 2,

    /// <summary>
    /// One-triggers-one-cancels-other - combines OTO and OCO logic.
    /// </summary>
    OneTriggerOneCancelOther = 3
}

/// <summary>
/// Blockchain network identifiers for Lighter protocol.
/// </summary>
public static class ChainId
{
    /// <summary>
    /// Mainnet chain ID (Polygon).
    /// </summary>
    public const int Mainnet = 304;

    /// <summary>
    /// Testnet chain ID (Polygon Mumbai).
    /// </summary>
    public const int Testnet = 300;
}

/// <summary>
/// Common constants used in order operations.
/// </summary>
public static class OrderConstants
{
    /// <summary>
    /// Value indicating no trigger price is set.
    /// </summary>
    public const int NilTriggerPrice = 0;

    /// <summary>
    /// Default order expiry (28 days from submission).
    /// Use this value to apply the default expiry.
    /// </summary>
    public const long Default28DayOrderExpiry = -1;

    /// <summary>
    /// Expiry for immediate-or-cancel orders.
    /// </summary>
    public const long DefaultIocExpiry = 0;

    /// <summary>
    /// Default authentication token expiry (10 minutes from creation).
    /// Use this value to apply the default token expiry.
    /// </summary>
    public const long Default10MinAuthExpiry = -1;

    /// <summary>
    /// USDC ticker scale (6 decimal places).
    /// Used for balance/collateral values in some contexts.
    /// </summary>
    public const decimal UsdcTickerScale = 1_000_000m;

    /// <summary>
    /// Price scale for Lighter DEX (2 decimal places).
    /// Prices are represented in cents (e.g., $2920.57 = 292057).
    /// </summary>
    public const decimal PriceScale = 100m;

    /// <summary>
    /// Base asset scale (8 decimal places).
    /// Used for order sizes (e.g., 0.01 ETH = 1,000,000).
    /// </summary>
    public const decimal BaseAssetScale = 100_000_000m;
}
