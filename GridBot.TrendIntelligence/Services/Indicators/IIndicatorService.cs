using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services.Indicators;

/// <summary>
/// Service for calculating technical indicators used in trading decisions.
/// </summary>
public interface IIndicatorService
{
    /// <summary>
    /// Calculates the Average True Range (ATR) for volatility measurement.
    /// </summary>
    /// <param name="candles">Candlestick data ordered by timestamp ascending.</param>
    /// <param name="period">Number of periods for ATR calculation. Default is 14.</param>
    /// <returns>ATR value, or 0 if insufficient data.</returns>
    decimal CalculateAtr(IReadOnlyList<CandlestickData> candles, int period = 14);

    /// <summary>
    /// Calculates the Exponential Moving Average (EMA) for trend detection.
    /// </summary>
    /// <param name="prices">Price values ordered by time ascending.</param>
    /// <param name="period">Number of periods for EMA calculation.</param>
    /// <returns>EMA value, or 0 if insufficient data.</returns>
    decimal CalculateEma(IReadOnlyList<decimal> prices, int period);

    /// <summary>
    /// Calculates the MACD (Moving Average Convergence Divergence) indicator.
    /// </summary>
    /// <param name="prices">Price values ordered by time ascending.</param>
    /// <param name="fastPeriod">Fast EMA period. Default is 12.</param>
    /// <param name="slowPeriod">Slow EMA period. Default is 26.</param>
    /// <param name="signalPeriod">Signal line EMA period. Default is 9.</param>
    /// <returns>MACD result with line, signal, and histogram values.</returns>
    MacdResult CalculateMacd(
        IReadOnlyList<decimal> prices,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9);

    /// <summary>
    /// Calculates the Average Directional Index (ADX) for trend strength.
    /// </summary>
    /// <param name="candles">Candlestick data ordered by timestamp ascending.</param>
    /// <param name="period">Number of periods for ADX calculation. Default is 14.</param>
    /// <returns>ADX value (0-100), or 0 if insufficient data.</returns>
    decimal CalculateAdx(IReadOnlyList<CandlestickData> candles, int period = 14);

    /// <summary>
    /// Calculates the Simple Moving Average (SMA) for price data.
    /// </summary>
    /// <param name="prices">Price values ordered by time ascending.</param>
    /// <param name="period">Number of periods for SMA calculation.</param>
    /// <returns>SMA value, or 0 if insufficient data.</returns>
    decimal CalculateSma(IReadOnlyList<decimal> prices, int period);
}
