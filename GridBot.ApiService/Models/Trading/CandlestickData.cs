namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Processed candlestick data with decimal values for calculations.
/// </summary>
public sealed class CandlestickData
{
    /// <summary>
    /// Candle open time.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Opening price.
    /// </summary>
    public decimal Open { get; init; }

    /// <summary>
    /// Highest price during the period.
    /// </summary>
    public decimal High { get; init; }

    /// <summary>
    /// Lowest price during the period.
    /// </summary>
    public decimal Low { get; init; }

    /// <summary>
    /// Closing price.
    /// </summary>
    public decimal Close { get; init; }

    /// <summary>
    /// Trading volume during the period.
    /// </summary>
    public decimal Volume { get; init; }
}
