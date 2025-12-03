using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.State;

/// <summary>
/// Event arguments for trend state change events.
/// </summary>
public sealed class TrendStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous trend state.
    /// </summary>
    public required TrendState PreviousState { get; init; }

    /// <summary>
    /// The new trend state.
    /// </summary>
    public required TrendState NewState { get; init; }

    /// <summary>
    /// When the trend state change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
