using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.State;

/// <summary>
/// Event arguments for trading state change events.
/// </summary>
public sealed class TradingStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous trading state.
    /// </summary>
    public required TradingState PreviousState { get; init; }

    /// <summary>
    /// The new trading state.
    /// </summary>
    public required TradingState NewState { get; init; }

    /// <summary>
    /// Reason for the state transition.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// When the state change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
