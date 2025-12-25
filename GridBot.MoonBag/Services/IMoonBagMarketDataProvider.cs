namespace GridBot.MoonBag.Services;

/// <summary>
/// Abstraction for market data access required by moon bag services.
/// Implemented by GridBot.ApiService to provide actual market data.
/// </summary>
public interface IMoonBagMarketDataProvider
{
    /// <summary>
    /// Gets the current market price.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current price.</returns>
    Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets historical closing prices for moving average calculation.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="interval">Candle interval (e.g., "1d").</param>
    /// <param name="count">Number of candles to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of closing prices ordered by time ascending.</returns>
    Task<IReadOnlyList<decimal>> GetClosingPricesAsync(int marketId, string interval, int count, CancellationToken ct = default);

    /// <summary>
    /// Calculates Simple Moving Average for given prices.
    /// </summary>
    /// <param name="prices">List of prices.</param>
    /// <param name="period">MA period.</param>
    /// <returns>SMA value.</returns>
    decimal CalculateSma(IReadOnlyList<decimal> prices, int period);
}
