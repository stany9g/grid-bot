namespace GridBot.MoonBag.Services;

/// <summary>
/// Trend state for moon bag release conditions.
/// </summary>
public enum MoonBagTrendState
{
    /// <summary>
    /// Strong bullish trend detected.
    /// </summary>
    StrongBull,

    /// <summary>
    /// Mild bullish trend detected.
    /// </summary>
    MildBull,

    /// <summary>
    /// No clear trend or low directional strength.
    /// </summary>
    Neutral,

    /// <summary>
    /// Mild bearish trend detected.
    /// </summary>
    MildBear,

    /// <summary>
    /// Strong bearish trend detected.
    /// Required for moon bag auto-release conditions.
    /// </summary>
    StrongBear
}

/// <summary>
/// Abstraction for trend state access required by moon bag services.
/// Implemented by GridBot.ApiService to provide actual trend data.
/// </summary>
public interface IMoonBagTrendProvider
{
    /// <summary>
    /// Gets the current trend state for a market.
    /// </summary>
    MoonBagTrendState CurrentTrendState { get; }
}
