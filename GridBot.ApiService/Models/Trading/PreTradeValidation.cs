namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of pre-trade validation against market depth.
/// Used to prevent order submission to thin order books that could result in excessive slippage.
/// </summary>
public sealed record PreTradeValidation
{
    /// <summary>
    /// Whether the order can proceed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Reason if invalid. Null when IsValid is true.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Recommended order size if original was too large.
    /// Null if no adjustment needed or order was rejected entirely.
    /// </summary>
    public decimal? RecommendedSizeUsd { get; init; }

    /// <summary>
    /// Available depth in USD on the relevant side (asks for buys, bids for sells).
    /// </summary>
    public required decimal AvailableDepthUsd { get; init; }

    /// <summary>
    /// Total order book depth in USD (both sides combined).
    /// </summary>
    public required decimal TotalDepthUsd { get; init; }

    /// <summary>
    /// Current bid-ask spread percentage.
    /// </summary>
    public required decimal SpreadPercent { get; init; }

    /// <summary>
    /// Age of the depth data used for validation.
    /// </summary>
    public required TimeSpan DataAge { get; init; }

    /// <summary>
    /// Timestamp of validation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    /// <param name="availableDepth">Depth on the relevant side in USD.</param>
    /// <param name="totalDepth">Total order book depth in USD.</param>
    /// <param name="spreadPercent">Current spread percentage.</param>
    /// <param name="dataAge">Age of the data used.</param>
    /// <returns>Valid pre-trade validation result.</returns>
    public static PreTradeValidation Valid(
        decimal availableDepth,
        decimal totalDepth,
        decimal spreadPercent,
        TimeSpan dataAge) => new()
    {
        IsValid = true,
        AvailableDepthUsd = availableDepth,
        TotalDepthUsd = totalDepth,
        SpreadPercent = spreadPercent,
        DataAge = dataAge
    };

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    /// <param name="reason">Reason for rejection.</param>
    /// <param name="availableDepth">Depth on the relevant side in USD.</param>
    /// <param name="totalDepth">Total order book depth in USD.</param>
    /// <param name="spreadPercent">Current spread percentage.</param>
    /// <param name="dataAge">Age of the data used.</param>
    /// <param name="recommendedSize">Recommended order size if applicable.</param>
    /// <returns>Invalid pre-trade validation result.</returns>
    public static PreTradeValidation Invalid(
        string reason,
        decimal availableDepth,
        decimal totalDepth,
        decimal spreadPercent,
        TimeSpan dataAge,
        decimal? recommendedSize = null) => new()
    {
        IsValid = false,
        Reason = reason,
        RecommendedSizeUsd = recommendedSize,
        AvailableDepthUsd = availableDepth,
        TotalDepthUsd = totalDepth,
        SpreadPercent = spreadPercent,
        DataAge = dataAge
    };
}
