namespace GridBot.ApiService.Services.Bot;

/// <summary>
/// Event arguments for bot status change events.
/// </summary>
public sealed class BotStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous status before the change.
    /// </summary>
    public required BotStatus PreviousStatus { get; init; }

    /// <summary>
    /// Gets the new current status.
    /// </summary>
    public required BotStatus NewStatus { get; init; }

    /// <summary>
    /// Gets the timestamp when the status changed.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets an optional message providing context for the status change.
    /// </summary>
    public string? Message { get; init; }
}
