using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Service for managing the complete lifecycle of a trading grid.
/// Handles initialization, updates, shifts, and pause/resume operations.
/// </summary>
public interface IGridLifecycleService
{
    /// <summary>
    /// Initializes a new grid for the specified market.
    /// Calculates parameters, generates levels, and places orders.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The initialized grid state.</returns>
    Task<GridState> InitializeGridAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of a grid.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current grid state, or null if not initialized.</returns>
    Task<GridState?> GetCurrentGridStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates the grid based on current market conditions.
    /// Syncs order status, detects fills, and adjusts grid as needed.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the update operation.</returns>
    Task<GridUpdateResult> UpdateGridAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Shifts the grid to a new center price.
    /// Cancels orders far from new center and places new orders.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="newCenterPrice">New center price for the grid.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ShiftGridAsync(int marketId, decimal newCenterPrice, CancellationToken ct = default);

    /// <summary>
    /// Pauses grid operations.
    /// Orders remain live but no new orders are placed.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseGridAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Resumes grid operations after a pause.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeGridAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Tears down the grid, cancelling all orders.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task TeardownGridAsync(int marketId, CancellationToken ct = default);
}
