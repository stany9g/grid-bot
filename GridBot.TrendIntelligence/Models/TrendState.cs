namespace GridBot.TrendIntelligence.Models;

/// <summary>
/// Represents the detected market trend state used for inventory skew targeting.
/// </summary>
/// <remarks>
/// Trend detection conditions:
/// - STRONG_BULL: EMA(20) > EMA(50) AND MACD > Signal AND MACD > 0 AND ADX > 25
/// - MILD_BULL: EMA(20) > EMA(50) AND (MACD > Signal OR MACD > 0)
/// - NEUTRAL: EMA(20) within 1% of EMA(50) OR ADX < 20
/// - MILD_BEAR: EMA(20) < EMA(50) AND (MACD < Signal OR MACD < 0)
/// - STRONG_BEAR: EMA(20) < EMA(50) AND MACD < Signal AND MACD < 0 AND ADX > 25
/// </remarks>
public enum TrendState
{
    /// <summary>
    /// Strong bullish trend detected. Target skew: 80% crypto / 20% USDT.
    /// </summary>
    StrongBull,

    /// <summary>
    /// Mild bullish trend detected. Target skew: 70% crypto / 30% USDT.
    /// </summary>
    MildBull,

    /// <summary>
    /// No clear trend or low directional strength. Target skew: 50% crypto / 50% USDT.
    /// </summary>
    Neutral,

    /// <summary>
    /// Mild bearish trend detected. Target skew: 30% crypto / 70% USDT.
    /// </summary>
    MildBear,

    /// <summary>
    /// Strong bearish trend detected. Target skew: 20% crypto / 80% USDT.
    /// </summary>
    StrongBear
}
