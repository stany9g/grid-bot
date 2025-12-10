using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Base record for all WebSocket messages.
/// </summary>
public abstract record WebSocketMessage
{
    /// <summary>
    /// Message type identifier.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Channel this message relates to.
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; init; }
}

/// <summary>
/// Subscription request message (client -> server).
/// </summary>
public sealed record SubscribeMessage : WebSocketMessage
{
    /// <summary>
    /// Authentication token for private channels.
    /// </summary>
    [JsonPropertyName("auth")]
    public string? Auth { get; init; }
}

/// <summary>
/// Unsubscription request message (client -> server).
/// </summary>
public sealed record UnsubscribeMessage : WebSocketMessage;

/// <summary>
/// Pong response message (client -> server).
/// </summary>
public sealed record PongMessage : WebSocketMessage;

/// <summary>
/// Ping message from server.
/// </summary>
public sealed record PingMessage : WebSocketMessage;

/// <summary>
/// Subscription confirmation from server.
/// </summary>
public sealed record SubscribedMessage : WebSocketMessage
{
    /// <summary>
    /// Message offset for resumption.
    /// </summary>
    [JsonPropertyName("offset")]
    public long Offset { get; init; }
}

/// <summary>
/// Error message from server.
/// </summary>
public sealed record ErrorMessage : WebSocketMessage
{
    /// <summary>
    /// Error code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
