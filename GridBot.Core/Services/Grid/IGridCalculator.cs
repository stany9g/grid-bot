using GridBot.Core.Models;

namespace GridBot.Core.Services.Grid;

/// <summary>
/// Calculates grid levels based on configuration.
/// Works with decimal values - exchange-specific scaling is handled by adapters.
/// </summary>
public interface IGridCalculator
{
    /// <summary>
    /// Calculates grid levels centered around a price.
    /// </summary>
    /// <param name="centerPrice">Center price for the grid.</param>
    /// <returns>List of grid levels (buy and sell).</returns>
    List<GridLevel> CalculateLevels(decimal centerPrice);

    /// <summary>
    /// Calculates the order size in base asset units for a given USDC amount.
    /// </summary>
    /// <param name="usdcAmount">Amount in USDC.</param>
    /// <param name="price">Price of the asset.</param>
    /// <returns>Size in base asset units.</returns>
    decimal CalculateOrderSize(decimal usdcAmount, decimal price);
}
