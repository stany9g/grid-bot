namespace GridBot.MoonBag.Services;

/// <summary>
/// Grid state information for trailing grid service.
/// </summary>
public sealed class MoonBagGridState
{
    /// <summary>
    /// Current grid center price.
    /// </summary>
    public decimal CenterPrice { get; init; }

    /// <summary>
    /// Current grid upper bound.
    /// </summary>
    public decimal UpperBound { get; init; }

    /// <summary>
    /// Current grid lower bound.
    /// </summary>
    public decimal LowerBound { get; init; }
}

/// <summary>
/// Abstraction for grid state access and manipulation.
/// Implemented by GridBot.ApiService to interact with the grid lifecycle service.
/// </summary>
public interface IMoonBagGridProvider
{
    /// <summary>
    /// Gets the current grid state for a market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Grid state or null if grid not initialized.</returns>
    Task<MoonBagGridState?> GetGridStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Shifts the grid to a new center price.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="newCenterPrice">New center price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ShiftGridAsync(int marketId, decimal newCenterPrice, CancellationToken ct = default);
}
