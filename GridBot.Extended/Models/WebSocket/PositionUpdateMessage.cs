namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Position update event for the realtime data provider.
/// </summary>
public sealed class PositionUpdateEvent
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Position size (positive for long, negative for short).
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Average entry price.
    /// </summary>
    public required decimal EntryPrice { get; init; }

    /// <summary>
    /// Current mark price.
    /// </summary>
    public required decimal MarkPrice { get; init; }

    /// <summary>
    /// Unrealized PnL.
    /// </summary>
    public required decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Liquidation price.
    /// </summary>
    public decimal? LiquidationPrice { get; init; }

    /// <summary>
    /// Current leverage.
    /// </summary>
    public int Leverage { get; init; }

    /// <summary>
    /// Whether this is a long position.
    /// </summary>
    public bool IsLong => Size > 0;

    /// <summary>
    /// Timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Account snapshot event combining all account data.
/// </summary>
public sealed class AccountUpdateEvent
{
    /// <summary>
    /// Account address.
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Total collateral.
    /// </summary>
    public decimal Collateral { get; init; }

    /// <summary>
    /// Available balance.
    /// </summary>
    public decimal AvailableBalance { get; init; }

    /// <summary>
    /// Portfolio value including unrealized PnL.
    /// </summary>
    public decimal PortfolioValue { get; init; }

    /// <summary>
    /// All open positions.
    /// </summary>
    public IReadOnlyList<PositionSnapshot>? Positions { get; init; }

    /// <summary>
    /// Timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Position snapshot within account update.
/// </summary>
public sealed class PositionSnapshot
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Position size.
    /// </summary>
    public decimal Size { get; init; }

    /// <summary>
    /// Average entry price.
    /// </summary>
    public decimal EntryPrice { get; init; }

    /// <summary>
    /// Unrealized PnL.
    /// </summary>
    public decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Liquidation price.
    /// </summary>
    public decimal? LiquidationPrice { get; init; }

    /// <summary>
    /// Leverage.
    /// </summary>
    public int Leverage { get; init; }
}
