namespace GridBot.Abstractions.Scaling;

/// <summary>
/// Provides scaling operations to convert between human-readable decimals and exchange-native integers.
/// Each exchange may have different precision requirements.
/// </summary>
public interface IScalingProvider
{
    /// <summary>
    /// Gets the scaling information for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The market scaling information.</returns>
    Task<MarketScaling> GetMarketScalingAsync(string marketId, CancellationToken ct = default);

    /// <summary>
    /// Converts a decimal price to the exchange-native scaled integer format.
    /// </summary>
    /// <param name="price">The human-readable price.</param>
    /// <param name="scaling">The market scaling information.</param>
    /// <returns>The scaled price as a long.</returns>
    long ScalePrice(decimal price, MarketScaling scaling);

    /// <summary>
    /// Converts a decimal amount to the exchange-native scaled integer format.
    /// </summary>
    /// <param name="amount">The human-readable amount.</param>
    /// <param name="scaling">The market scaling information.</param>
    /// <returns>The scaled amount as a long.</returns>
    long ScaleAmount(decimal amount, MarketScaling scaling);

    /// <summary>
    /// Converts an exchange-native scaled price back to a decimal.
    /// </summary>
    /// <param name="scaledPrice">The scaled price.</param>
    /// <param name="scaling">The market scaling information.</param>
    /// <returns>The human-readable price.</returns>
    decimal UnscalePrice(long scaledPrice, MarketScaling scaling);

    /// <summary>
    /// Converts an exchange-native scaled amount back to a decimal.
    /// </summary>
    /// <param name="scaledAmount">The scaled amount.</param>
    /// <param name="scaling">The market scaling information.</param>
    /// <returns>The human-readable amount.</returns>
    decimal UnscaleAmount(long scaledAmount, MarketScaling scaling);
}
