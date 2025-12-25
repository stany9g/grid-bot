namespace GridBot.Abstractions.Models.Market;

/// <summary>
/// Represents OHLCV candlestick data for a specific time period.
/// </summary>
public sealed record CandlestickData
{
    /// <summary>
    /// Gets the timestamp for the start of this candle.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the opening price.
    /// </summary>
    public required decimal Open { get; init; }

    /// <summary>
    /// Gets the highest price during the period.
    /// </summary>
    public required decimal High { get; init; }

    /// <summary>
    /// Gets the lowest price during the period.
    /// </summary>
    public required decimal Low { get; init; }

    /// <summary>
    /// Gets the closing price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// Gets the trading volume during the period.
    /// </summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// Gets the True Range for ATR calculation: max(High-Low, |High-PrevClose|, |Low-PrevClose|).
    /// This should be calculated by the consumer when previous close is available.
    /// </summary>
    public decimal Range => High - Low;

    /// <summary>
    /// Gets whether the candle is bullish (close > open).
    /// </summary>
    public bool IsBullish => Close > Open;

    /// <summary>
    /// Gets whether the candle is bearish (close less than open).
    /// </summary>
    public bool IsBearish => Close < Open;

    /// <summary>
    /// Gets the body size (absolute difference between open and close).
    /// </summary>
    public decimal BodySize => Math.Abs(Close - Open);

    /// <summary>
    /// Gets the upper wick size.
    /// </summary>
    public decimal UpperWick => High - Math.Max(Open, Close);

    /// <summary>
    /// Gets the lower wick size.
    /// </summary>
    public decimal LowerWick => Math.Min(Open, Close) - Low;
}
