namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Service for scaling prices and amounts according to market-specific decimal precision.
/// </summary>
public interface IMarketScalingService
{
    /// <summary>
    /// Converts a decimal price to the scaled integer value for the Lighter API.
    /// </summary>
    /// <param name="price">Decimal price (e.g., 2920.57)</param>
    /// <param name="marketId">Market ID to get the correct decimal precision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scaled price as long (e.g., 292057 for 2 decimals).</returns>
    Task<long> ScalePriceAsync(decimal price, int marketId, CancellationToken ct = default);

    /// <summary>
    /// Converts a decimal amount to the scaled integer base amount for the Lighter API.
    /// </summary>
    /// <param name="amount">Decimal amount (e.g., 0.01 ETH)</param>
    /// <param name="marketId">Market ID to get the correct decimal precision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scaled base amount as long (e.g., 1000000 for 8 decimals).</returns>
    Task<long> ScaleBaseAmountAsync(decimal amount, int marketId, CancellationToken ct = default);

    /// <summary>
    /// Converts a scaled integer price back to decimal.
    /// </summary>
    /// <param name="scaledPrice">Scaled price from the API.</param>
    /// <param name="marketId">Market ID to get the correct decimal precision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decimal price.</returns>
    Task<decimal> UnscalePriceAsync(long scaledPrice, int marketId, CancellationToken ct = default);

    /// <summary>
    /// Converts a scaled integer base amount back to decimal.
    /// </summary>
    /// <param name="scaledAmount">Scaled amount from the API.</param>
    /// <param name="marketId">Market ID to get the correct decimal precision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decimal amount.</returns>
    Task<decimal> UnscaleBaseAmountAsync(long scaledAmount, int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the market metadata including decimal precision.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Market metadata.</returns>
    Task<MarketMetadata> GetMarketMetadataAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Preloads market metadata for faster subsequent calls.
    /// Call this during startup for known markets.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task PreloadMarketsAsync(CancellationToken ct = default);
}

/// <summary>
/// Market metadata including decimal precision for scaling.
/// </summary>
public sealed class MarketMetadata
{
    /// <summary>
    /// Market ID.
    /// </summary>
    public int MarketId { get; init; }

    /// <summary>
    /// Market symbol (e.g., "ETH", "BTC").
    /// </summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Number of decimal places for prices.
    /// </summary>
    public int SupportedPriceDecimals { get; init; }

    /// <summary>
    /// Number of decimal places for sizes (base amounts).
    /// Used for scaling decimal amounts to integers.
    /// </summary>
    public int SupportedSizeDecimals { get; init; }

    /// <summary>
    /// Internal size decimals from the API (may differ from SupportedSizeDecimals).
    /// Used to calculate the lot size increment.
    /// </summary>
    public int SizeDecimals { get; init; }

    /// <summary>
    /// Minimum base amount for orders (already scaled).
    /// </summary>
    public decimal MinBaseAmount { get; init; }

    /// <summary>
    /// Minimum quote amount for orders (already scaled).
    /// </summary>
    public decimal MinQuoteAmount { get; init; }

    /// <summary>
    /// Multiplier for scaling prices: 10^SupportedPriceDecimals.
    /// </summary>
    public decimal PriceMultiplier => (decimal)Math.Pow(10, SupportedPriceDecimals);

    /// <summary>
    /// Multiplier for scaling sizes: 10^SupportedSizeDecimals.
    /// </summary>
    public decimal SizeMultiplier => (decimal)Math.Pow(10, SupportedSizeDecimals);

    /// <summary>
    /// Lot size in scaled units. All order amounts must be multiples of this value.
    /// Calculated as 10^(SupportedSizeDecimals - SizeDecimals).
    /// </summary>
    public long LotSize => SupportedSizeDecimals >= SizeDecimals
        ? (long)Math.Pow(10, SupportedSizeDecimals - SizeDecimals)
        : 1;
}
