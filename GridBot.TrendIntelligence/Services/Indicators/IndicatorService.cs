using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services.Indicators;

/// <summary>
/// Implementation of technical indicator calculations.
/// Thread-safe stateless service.
/// </summary>
public sealed class IndicatorService : IIndicatorService
{
    /// <inheritdoc />
    public decimal CalculateAtr(IReadOnlyList<CandlestickData> candles, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count < 2)
            return 0m;

        var trueRanges = new List<decimal>(candles.Count - 1);

        for (var i = 1; i < candles.Count; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];

            var highLow = current.High - current.Low;
            var highPrevClose = Math.Abs(current.High - previous.Close);
            var lowPrevClose = Math.Abs(current.Low - previous.Close);

            var trueRange = Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
            trueRanges.Add(trueRange);
        }

        if (trueRanges.Count < period)
            return trueRanges.Count > 0 ? trueRanges.Average() : 0m;

        // Calculate initial SMA for first ATR value
        var initialAtr = trueRanges.Take(period).Average();

        if (trueRanges.Count == period)
            return initialAtr;

        // Apply Wilder's smoothing (EMA-like) for remaining values
        var atr = initialAtr;
        for (var i = period; i < trueRanges.Count; i++)
        {
            atr = ((atr * (period - 1)) + trueRanges[i]) / period;
        }

        return atr;
    }

    /// <inheritdoc />
    public decimal CalculateEma(IReadOnlyList<decimal> prices, int period)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (prices.Count == 0 || period <= 0)
            return 0m;

        if (prices.Count < period)
            return prices.Average();

        // EMA multiplier: k = 2 / (period + 1)
        var multiplier = 2m / (period + 1);

        // Start with SMA for first EMA value
        var ema = prices.Take(period).Average();

        // Calculate EMA for remaining prices
        for (var i = period; i < prices.Count; i++)
        {
            ema = (prices[i] * multiplier) + (ema * (1 - multiplier));
        }

        return ema;
    }

    /// <inheritdoc />
    public MacdResult CalculateMacd(
        IReadOnlyList<decimal> prices,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (prices.Count < slowPeriod)
        {
            return new MacdResult
            {
                MacdLine = 0m,
                SignalLine = 0m,
                Histogram = 0m
            };
        }

        // Calculate MACD line values for signal calculation
        var macdValues = CalculateMacdSeries(prices, fastPeriod, slowPeriod);

        if (macdValues.Count == 0)
        {
            return new MacdResult
            {
                MacdLine = 0m,
                SignalLine = 0m,
                Histogram = 0m
            };
        }

        var macdLine = macdValues[^1];
        var signalLine = CalculateEma(macdValues, signalPeriod);
        var histogram = macdLine - signalLine;

        return new MacdResult
        {
            MacdLine = macdLine,
            SignalLine = signalLine,
            Histogram = histogram
        };
    }

    /// <inheritdoc />
    public decimal CalculateSma(IReadOnlyList<decimal> prices, int period)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (prices.Count == 0 || period <= 0)
            return 0m;

        if (prices.Count < period)
            return prices.Average();

        // Take the last 'period' prices and calculate average
        return prices.Skip(prices.Count - period).Take(period).Average();
    }

    /// <inheritdoc />
    public decimal CalculateAdx(IReadOnlyList<CandlestickData> candles, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count < period + 1)
            return 0m;

        var plusDmValues = new List<decimal>();
        var minusDmValues = new List<decimal>();
        var trueRangeValues = new List<decimal>();

        // Calculate +DM, -DM, and TR for each period
        for (var i = 1; i < candles.Count; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];

            var upMove = current.High - previous.High;
            var downMove = previous.Low - current.Low;

            var plusDm = upMove > downMove && upMove > 0 ? upMove : 0m;
            var minusDm = downMove > upMove && downMove > 0 ? downMove : 0m;

            plusDmValues.Add(plusDm);
            minusDmValues.Add(minusDm);

            var highLow = current.High - current.Low;
            var highPrevClose = Math.Abs(current.High - previous.Close);
            var lowPrevClose = Math.Abs(current.Low - previous.Close);
            var tr = Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
            trueRangeValues.Add(tr);
        }

        if (plusDmValues.Count < period)
            return 0m;

        // Calculate smoothed averages using Wilder's smoothing
        var smoothedPlusDm = WilderSmooth(plusDmValues, period);
        var smoothedMinusDm = WilderSmooth(minusDmValues, period);
        var smoothedTr = WilderSmooth(trueRangeValues, period);

        if (smoothedTr == 0)
            return 0m;

        // Calculate +DI and -DI
        var plusDi = (smoothedPlusDm / smoothedTr) * 100m;
        var minusDi = (smoothedMinusDm / smoothedTr) * 100m;

        // Calculate DX
        var diSum = plusDi + minusDi;
        if (diSum == 0)
            return 0m;

        var dx = (Math.Abs(plusDi - minusDi) / diSum) * 100m;

        // For a simple implementation, return DX
        // A more complete ADX would smooth DX over another period
        return dx;
    }

    private static List<decimal> CalculateMacdSeries(IReadOnlyList<decimal> prices, int fastPeriod, int slowPeriod)
    {
        var result = new List<decimal>();

        if (prices.Count < slowPeriod)
            return result;

        // Validate we have meaningful data
        if (prices.All(p => p == 0))
            return result;

        var fastMultiplier = 2m / (fastPeriod + 1);
        var slowMultiplier = 2m / (slowPeriod + 1);

        // Initialize EMAs with SMA
        var fastEma = prices.Take(fastPeriod).Average();
        var slowEma = prices.Take(slowPeriod).Average();

        // Warm up fast EMA from fastPeriod to slowPeriod
        for (var i = fastPeriod; i < slowPeriod; i++)
        {
            fastEma = (prices[i] * fastMultiplier) + (fastEma * (1 - fastMultiplier));
        }

        // Now calculate MACD values
        for (var i = slowPeriod; i < prices.Count; i++)
        {
            fastEma = (prices[i] * fastMultiplier) + (fastEma * (1 - fastMultiplier));
            slowEma = (prices[i] * slowMultiplier) + (slowEma * (1 - slowMultiplier));
            result.Add(fastEma - slowEma);
        }

        return result;
    }

    private static decimal WilderSmooth(List<decimal> values, int period)
    {
        if (values.Count < period)
            return values.Count > 0 ? values.Average() : 0m;

        // First smoothed value is sum of first 'period' values
        var smoothed = values.Take(period).Sum();

        // Apply Wilder's smoothing
        for (var i = period; i < values.Count; i++)
        {
            smoothed = smoothed - (smoothed / period) + values[i];
        }

        // Return average, not sum (divide by period)
        return smoothed / period;
    }
}
