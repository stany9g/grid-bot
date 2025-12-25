using GridBot.AdvancedRisk.Models;
namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Monitors order book liquidity depth.
/// </summary>
public interface ILiquidityMonitor
{
    /// <summary>
    /// Gets the current liquidity status for a market.
    /// </summary>
    Task<LiquidityStatus> GetLiquidityStatusAsync(int marketId, CancellationToken ct = default);
    /// <summary>
    /// Refreshes liquidity data for a market.
    /// </summary>
    Task RefreshLiquidityAsync(int marketId, CancellationToken ct = default);
}
