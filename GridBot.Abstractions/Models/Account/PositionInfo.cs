using GridBot.Abstractions.Models.Enums;

namespace GridBot.Abstractions.Models.Account;

/// <summary>
/// Represents an open position on a market.
/// </summary>
public sealed record PositionInfo
{
    /// <summary>
    /// Gets the market identifier for this position.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the position size (positive for long, negative for short).
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Gets the average entry price for this position.
    /// </summary>
    public required decimal EntryPrice { get; init; }

    /// <summary>
    /// Gets the current mark price of the position.
    /// </summary>
    public required decimal MarkPrice { get; init; }

    /// <summary>
    /// Gets the unrealized PnL for this position.
    /// </summary>
    public required decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Gets the realized PnL for this position.
    /// </summary>
    public decimal RealizedPnl { get; init; }

    /// <summary>
    /// Gets the leverage applied to this position.
    /// </summary>
    public required int Leverage { get; init; }

    /// <summary>
    /// Gets the liquidation price for this position.
    /// </summary>
    public decimal? LiquidationPrice { get; init; }

    /// <summary>
    /// Gets the margin mode for this position.
    /// </summary>
    public MarginMode MarginMode { get; init; } = MarginMode.Cross;

    /// <summary>
    /// Gets whether this is a long position.
    /// </summary>
    public bool IsLong => Size > 0;

    /// <summary>
    /// Gets whether this is a short position.
    /// </summary>
    public bool IsShort => Size < 0;

    /// <summary>
    /// Gets the absolute position size.
    /// </summary>
    public decimal AbsoluteSize => Math.Abs(Size);

    /// <summary>
    /// Gets the notional value of the position (size * mark price).
    /// </summary>
    public decimal NotionalValue => AbsoluteSize * MarkPrice;
}
