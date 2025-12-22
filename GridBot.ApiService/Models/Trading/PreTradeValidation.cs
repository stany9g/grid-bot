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

/// <summary>
/// Result of PostOnly price validation against current spread.
/// Ensures orders won't cross the spread and get rejected by exchange.
/// </summary>
public sealed record PostOnlyValidation
{
    /// <summary>
    /// Whether the order price is valid (won't cross spread).
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Reason if invalid. Null when IsValid is true.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The order price that was validated.
    /// </summary>
    public required decimal OrderPrice { get; init; }

    /// <summary>
    /// Best bid price (highest buy order).
    /// </summary>
    public required decimal BestBid { get; init; }

    /// <summary>
    /// Best ask price (lowest sell order).
    /// </summary>
    public required decimal BestAsk { get; init; }

    /// <summary>
    /// Recommended price that would not cross the spread.
    /// For BUY: just below best ask. For SELL: just above best bid.
    /// Null if order is valid or no adjustment possible.
    /// </summary>
    public decimal? RecommendedPrice { get; init; }

    /// <summary>
    /// How far the order price is from crossing (negative = would cross).
    /// For BUY: bestAsk - orderPrice. For SELL: orderPrice - bestBid.
    /// </summary>
    public required decimal DistanceFromCrossing { get; init; }

    /// <summary>
    /// Age of the order book data used for validation.
    /// </summary>
    public required TimeSpan DataAge { get; init; }

    /// <summary>
    /// Timestamp of validation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a valid result (order won't cross spread).
    /// </summary>
    public static PostOnlyValidation Valid(
        decimal orderPrice,
        decimal bestBid,
        decimal bestAsk,
        decimal distanceFromCrossing,
        TimeSpan dataAge) => new()
    {
        IsValid = true,
        OrderPrice = orderPrice,
        BestBid = bestBid,
        BestAsk = bestAsk,
        DistanceFromCrossing = distanceFromCrossing,
        DataAge = dataAge
    };

    /// <summary>
    /// Creates an invalid result (order would cross spread).
    /// </summary>
    public static PostOnlyValidation WouldCross(
        string reason,
        decimal orderPrice,
        decimal bestBid,
        decimal bestAsk,
        decimal distanceFromCrossing,
        TimeSpan dataAge,
        decimal? recommendedPrice = null) => new()
    {
        IsValid = false,
        Reason = reason,
        OrderPrice = orderPrice,
        BestBid = bestBid,
        BestAsk = bestAsk,
        DistanceFromCrossing = distanceFromCrossing,
        RecommendedPrice = recommendedPrice,
        DataAge = dataAge
    };

    /// <summary>
    /// Creates an invalid result when order book data is unavailable.
    /// </summary>
    public static PostOnlyValidation NoData(string reason, decimal orderPrice) => new()
    {
        IsValid = false,
        Reason = reason,
        OrderPrice = orderPrice,
        BestBid = 0,
        BestAsk = 0,
        DistanceFromCrossing = 0,
        DataAge = TimeSpan.MaxValue
    };
}
