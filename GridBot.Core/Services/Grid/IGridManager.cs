using GridBot.Core.Models;

namespace GridBot.Core.Services.Grid;

/// <summary>
/// Manages grid orders on the exchange.
/// </summary>
public interface IGridManager
{
    /// <summary>
    /// Gets the current grid state.
    /// </summary>
    GridState State { get; }

    /// <summary>
    /// Initializes the grid around the current price.
    /// Cancels any existing orders and places new grid orders.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(decimal currentPrice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the grid based on current price.
    /// Adjusts orders as needed when fills occur.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateGridAsync(decimal currentPrice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the grid and cancels all orders.
    /// </summary>
    /// <param name="reason">Reason for pausing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PauseAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the grid after a pause.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResumeAsync(decimal currentPrice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all grid orders.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CancelAllOrdersAsync(CancellationToken cancellationToken = default);
}
