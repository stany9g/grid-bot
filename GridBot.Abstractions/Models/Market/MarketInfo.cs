namespace GridBot.Abstractions.Models.Market;

/// <summary>
/// Represents information about a tradable market.
/// </summary>
public sealed record MarketInfo
{
    /// <summary>
    /// Gets the unique market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the human-readable market symbol (e.g., "BTC-PERP", "ETH/USDC").
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Gets the base asset symbol (e.g., "BTC", "ETH").
    /// </summary>
    public required string BaseAsset { get; init; }

    /// <summary>
    /// Gets the quote asset symbol (e.g., "USDC", "USD").
    /// </summary>
    public required string QuoteAsset { get; init; }

    /// <summary>
    /// Gets whether this is a perpetual contract.
    /// </summary>
    public bool IsPerpetual { get; init; } = true;

    /// <summary>
    /// Gets the minimum order size.
    /// </summary>
    public required decimal MinOrderSize { get; init; }

    /// <summary>
    /// Gets the minimum price tick size.
    /// </summary>
    public required decimal TickSize { get; init; }

    /// <summary>
    /// Gets the order size step (minimum increment).
    /// </summary>
    public required decimal StepSize { get; init; }

    /// <summary>
    /// Gets the maximum leverage allowed.
    /// </summary>
    public int MaxLeverage { get; init; } = 20;

    /// <summary>
    /// Gets whether the market is currently active for trading.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Gets the maker fee rate (as a decimal, e.g., 0.0002 for 0.02%).
    /// </summary>
    public decimal MakerFeeRate { get; init; }

    /// <summary>
    /// Gets the taker fee rate (as a decimal, e.g., 0.0005 for 0.05%).
    /// </summary>
    public decimal TakerFeeRate { get; init; }
}
