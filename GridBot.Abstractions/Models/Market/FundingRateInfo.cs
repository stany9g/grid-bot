namespace GridBot.Abstractions.Models.Market;

/// <summary>
/// Represents funding rate information for a perpetual market.
/// </summary>
public sealed record FundingRateInfo
{
    /// <summary>
    /// Gets the market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the current funding rate (as a decimal, positive = longs pay shorts).
    /// </summary>
    public required decimal FundingRate { get; init; }

    /// <summary>
    /// Gets the timestamp of the next funding payment.
    /// </summary>
    public required DateTimeOffset NextFundingTime { get; init; }

    /// <summary>
    /// Gets the predicted funding rate for the next period.
    /// </summary>
    public decimal? PredictedRate { get; init; }

    /// <summary>
    /// Gets the funding interval in hours.
    /// </summary>
    public int FundingIntervalHours { get; init; } = 8;

    /// <summary>
    /// Gets the annualized funding rate.
    /// </summary>
    public decimal AnnualizedRate => FundingRate * (365m * 24m / FundingIntervalHours);

    /// <summary>
    /// Gets whether the funding is positive (longs pay shorts).
    /// </summary>
    public bool IsPositive => FundingRate > 0;
}
