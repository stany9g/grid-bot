namespace GridBot.Abstractions.Communication;

/// <summary>
/// Event arguments for connection health changes.
/// </summary>
public sealed class ConnectionHealthEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous connection state.
    /// </summary>
    public required ConnectionState PreviousState { get; init; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public required ConnectionState CurrentState { get; init; }

    /// <summary>
    /// Gets the reason for the state change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the timestamp when the state change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets whether the connection is now healthy.
    /// </summary>
    public bool IsHealthy => CurrentState == ConnectionState.Connected;

    /// <summary>
    /// Gets whether this represents a reconnection.
    /// </summary>
    public bool IsReconnection =>
        PreviousState is ConnectionState.Disconnected or ConnectionState.Reconnecting or ConnectionState.Failed
        && CurrentState == ConnectionState.Connected;
}
