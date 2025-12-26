using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Base class for WebSocket messages from Extended.
/// </summary>
public abstract record WebSocketMessageBase
{
    /// <summary>
    /// Message type identifier.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Channel this message belongs to.
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    /// <summary>
    /// Timestamp of the message (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Connection state for Extended WebSocket.
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

/// <summary>
/// Event raised when connection state changes.
/// </summary>
public sealed class ConnectionStateEvent
{
    /// <summary>
    /// The new connection state.
    /// </summary>
    public required ConnectionState State { get; init; }

    /// <summary>
    /// Reason for the state change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Number of reconnection attempts (if reconnecting).
    /// </summary>
    public int ReconnectAttempt { get; init; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
