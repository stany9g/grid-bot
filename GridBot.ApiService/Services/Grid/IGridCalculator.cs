using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Service for calculating grid parameters and levels based on market conditions.
/// </summary>
public interface IGridCalculator
{
    /// <summary>
    /// Calculates grid parameters based on current market conditions.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="atr">Average True Range value.</param>
    /// <param name="atrPercent">ATR as percentage of current price.</param>
    /// <returns>Calculated grid parameters.</returns>
    GridParameters CalculateGridParameters(decimal currentPrice, decimal atr, decimal atrPercent);

    /// <summary>
    /// Calculates individual grid levels based on parameters.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="parameters">Grid parameters to use.</param>
    /// <param name="clusters">Optional liquidity clusters to bias levels toward.</param>
    /// <returns>List of grid levels for both bids and asks.</returns>
    List<GridLevel> CalculateGridLevels(
        decimal currentPrice,
        GridParameters parameters,
        List<LiquidityCluster>? clusters = null);

    /// <summary>
    /// Calculates grid spacing percentage from ATR percentage.
    /// </summary>
    /// <param name="atrPercent">ATR as percentage of price.</param>
    /// <returns>Grid spacing as percentage.</returns>
    decimal CalculateGridSpacingFromAtr(decimal atrPercent);
}
