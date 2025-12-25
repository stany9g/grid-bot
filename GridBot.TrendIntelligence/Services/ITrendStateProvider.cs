using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services;

/// <summary>
/// Abstraction for trend state access and persistence.
/// Implemented by GridBot.ApiService to provide actual state management.
/// </summary>
public interface ITrendStateProvider
{
    /// <summary>
    /// Gets the current confirmed trend state.
    /// </summary>
    TrendState CurrentTrendState { get; }

    /// <summary>
    /// Gets the current inventory state.
    /// </summary>
    InventoryState CurrentInventory { get; }

    /// <summary>
    /// Updates the trend state.
    /// </summary>
    /// <param name="newState">New trend state.</param>
    Task UpdateTrendStateAsync(TrendState newState);

    /// <summary>
    /// Updates the inventory state.
    /// </summary>
    /// <param name="inventory">New inventory state.</param>
    Task UpdateInventoryAsync(InventoryState inventory);
}
