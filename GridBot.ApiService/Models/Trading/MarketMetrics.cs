namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Aggregated market metrics for trading decisions.
/// </summary>
public sealed class MarketMetrics
{
    /// <summary>
    /// Lighter DEX market identifier.
    /// </summary>
    public int MarketId { get; set; }

    /// <summary>
    /// Current market price.
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 14-period Average True Range as percentage.
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? Atr14 { get; set; }

    /// <summary>
    /// 20-period Exponential Moving Average.
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? Ema20 { get; set; }

    /// <summary>
    /// 50-period Exponential Moving Average.
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? Ema50 { get; set; }

    /// <summary>
    /// MACD line value (12, 26).
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? MacdLine { get; set; }

    /// <summary>
    /// MACD signal line value (9-period EMA of MACD).
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? MacdSignal { get; set; }

    /// <summary>
    /// MACD histogram (MACD line - Signal line).
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? MacdHistogram { get; set; }

    /// <summary>
    /// Average Directional Index (ADX) for trend strength.
    /// Null if insufficient data for calculation.
    /// </summary>
    public decimal? Adx { get; set; }

    /// <summary>
    /// 24-hour trading volume in USD.
    /// </summary>
    public decimal Volume24h { get; set; }

    /// <summary>
    /// 7-day average trading volume in USD.
    /// </summary>
    public decimal Volume7dAvg { get; set; }

    /// <summary>
    /// Total bid depth in USD at current order book snapshot.
    /// </summary>
    public decimal OrderBookBidDepth { get; set; }

    /// <summary>
    /// Total ask depth in USD at current order book snapshot.
    /// </summary>
    public decimal OrderBookAskDepth { get; set; }

    /// <summary>
    /// Current bid-ask spread as percentage.
    /// </summary>
    public decimal BidAskSpread { get; set; }

    /// <summary>
    /// Current funding rate (for perpetual futures).
    /// Null if not applicable or unavailable.
    /// </summary>
    public decimal? FundingRate { get; set; }

    /// <summary>
    /// When these metrics were last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Checks if all required indicators are available for trend detection.
    /// </summary>
    public bool HasAllIndicators =>
        Ema20.HasValue &&
        Ema50.HasValue &&
        MacdLine.HasValue &&
        MacdSignal.HasValue &&
        Adx.HasValue;

    /// <summary>
    /// Calculates the order book imbalance ratio (bid/ask).
    /// Greater than 1 = more bid depth, less than 1 = more ask depth.
    /// </summary>
    public decimal GetOrderBookImbalance()
    {
        if (OrderBookAskDepth <= 0)
            return decimal.MaxValue;

        return OrderBookBidDepth / OrderBookAskDepth;
    }

    /// <summary>
    /// Calculates volume relative to 7-day average as percentage.
    /// </summary>
    public decimal GetVolumeRatio()
    {
        if (Volume7dAvg <= 0)
            return 0;

        return (Volume24h / Volume7dAvg) * 100m;
    }

    /// <summary>
    /// Creates a copy of these metrics.
    /// </summary>
    public MarketMetrics Clone()
    {
        return new MarketMetrics
        {
            MarketId = MarketId,
            CurrentPrice = CurrentPrice,
            Atr14 = Atr14,
            Ema20 = Ema20,
            Ema50 = Ema50,
            MacdLine = MacdLine,
            MacdSignal = MacdSignal,
            MacdHistogram = MacdHistogram,
            Adx = Adx,
            Volume24h = Volume24h,
            Volume7dAvg = Volume7dAvg,
            OrderBookBidDepth = OrderBookBidDepth,
            OrderBookAskDepth = OrderBookAskDepth,
            BidAskSpread = BidAskSpread,
            FundingRate = FundingRate,
            LastUpdated = LastUpdated
        };
    }
}
