using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.State;

/// <summary>
/// Service for managing trading bot state transitions.
/// Thread-safe for concurrent access.
/// </summary>
public interface ITradingStateService
{
    /// <summary>
    /// Gets the current trading state.
    /// </summary>
    TradingState CurrentState { get; }

    /// <summary>
    /// Gets the current trend state.
    /// </summary>
    TrendState CurrentTrendState { get; }

    /// <summary>
    /// Gets a copy of the current inventory state.
    /// </summary>
    InventoryState CurrentInventory { get; }

    /// <summary>
    /// Gets when the current trading state started.
    /// </summary>
    DateTimeOffset StateStartedAt { get; }

    /// <summary>
    /// Attempts to transition to a new trading state.
    /// </summary>
    /// <param name="newState">The target state.</param>
    /// <param name="reason">Reason for the transition.</param>
    /// <returns>True if transition was successful, false if invalid transition.</returns>
    Task<bool> TransitionToAsync(TradingState newState, string reason);

    /// <summary>
    /// Updates the current trend state.
    /// </summary>
    /// <param name="newState">The new trend state.</param>
    Task UpdateTrendStateAsync(TrendState newState);

    /// <summary>
    /// Updates the current inventory state.
    /// </summary>
    /// <param name="inventory">The new inventory state.</param>
    Task UpdateInventoryAsync(InventoryState inventory);

    /// <summary>
    /// Checks if a state transition is valid.
    /// </summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Target state.</param>
    /// <returns>True if transition is allowed.</returns>
    bool IsValidTransition(TradingState from, TradingState to);

    /// <summary>
    /// Event raised when trading state changes.
    /// </summary>
    /// <remarks>
    /// <para>IMPORTANT: Subscribers MUST unsubscribe when no longer needed to prevent memory leaks.</para>
    /// <para>This service is registered as a singleton, so any subscribed handlers will be held
    /// for the lifetime of the application unless explicitly unsubscribed.</para>
    /// </remarks>
    event EventHandler<TradingStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when trend state changes.
    /// </summary>
    /// <remarks>
    /// <para>IMPORTANT: Subscribers MUST unsubscribe when no longer needed to prevent memory leaks.</para>
    /// <para>This service is registered as a singleton, so any subscribed handlers will be held
    /// for the lifetime of the application unless explicitly unsubscribed.</para>
    /// </remarks>
    event EventHandler<TrendStateChangedEventArgs>? TrendStateChanged;

    /// <summary>
    /// Loads persisted trading state from storage.
    /// Call this on startup to restore state across restarts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if state was loaded, false if no persisted state exists.</returns>
    Task<bool> LoadPersistedStateAsync(CancellationToken ct = default);
}
