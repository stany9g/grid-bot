namespace GridBot.Abstractions.Communication;

/// <summary>
/// Represents the current state of an exchange connection.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Connection has not been established.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// Connection is being established.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// Connection is established and operational.
    /// </summary>
    Connected = 2,

    /// <summary>
    /// Connection is being reconnected after a failure.
    /// </summary>
    Reconnecting = 3,

    /// <summary>
    /// Connection failed and is not recoverable.
    /// </summary>
    Failed = 4
}
