using System.Threading.Channels;

namespace GridBot.Lighter;

/// <summary>
/// Configuration options for the Lighter WebSocket client.
/// </summary>
public sealed class WebSocketOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "LighterWebSocket";

    /// <summary>
    /// WebSocket URL. Auto-derived from ApiUrl if not set.
    /// Mainnet: wss://mainnet.zklighter.elliot.ai/stream
    /// Testnet: wss://testnet.zklighter.elliot.ai/stream
    /// </summary>
    public string? WebSocketUrl { get; set; }

    /// <summary>
    /// Initial reconnection delay in milliseconds.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum reconnection delay in milliseconds.
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 60000;

    /// <summary>
    /// Auth token refresh interval in seconds (should be less than token validity of 600s).
    /// </summary>
    public int AuthTokenRefreshSeconds { get; set; } = 480;

    /// <summary>
    /// Channel capacity for bounded channels.
    /// </summary>
    public int ChannelCapacity { get; set; } = 100;

    /// <summary>
    /// Behavior when channel is full. DropOldest recommended for trading data.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.DropOldest;

    /// <summary>
    /// Maximum reconnection attempts before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 100;

    /// <summary>
    /// Ping timeout in seconds. Connection considered dead if no pong received.
    /// </summary>
    public int PingTimeoutSeconds { get; set; } = 30;
}
