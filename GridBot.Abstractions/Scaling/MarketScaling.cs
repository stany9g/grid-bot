namespace GridBot.Abstractions.Scaling;

/// <summary>
/// Contains scaling information for a market to convert between human-readable and exchange-native formats.
/// Different exchanges use different precision and scaling factors for prices and amounts.
/// </summary>
public sealed record MarketScaling
{
    /// <summary>
    /// Gets the market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the price scale factor (e.g., 1_000_000 for 6 decimal places).
    /// </summary>
    public required long PriceScale { get; init; }

    /// <summary>
    /// Gets the amount/size scale factor.
    /// </summary>
    public required long AmountScale { get; init; }

    /// <summary>
    /// Gets the number of decimal places for prices.
    /// </summary>
    public required int PriceDecimals { get; init; }

    /// <summary>
    /// Gets the number of decimal places for amounts.
    /// </summary>
    public required int AmountDecimals { get; init; }

    /// <summary>
    /// Gets the minimum tick size for prices.
    /// </summary>
    public required decimal MinTickSize { get; init; }

    /// <summary>
    /// Gets the minimum step size for amounts.
    /// </summary>
    public required decimal MinStepSize { get; init; }

    /// <summary>
    /// Gets the minimum order size in base asset.
    /// </summary>
    public decimal MinOrderSize { get; init; }

    /// <summary>
    /// Gets the maximum order size in base asset (if applicable).
    /// </summary>
    public decimal? MaxOrderSize { get; init; }
}
