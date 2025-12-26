using System.Threading.Channels;

namespace GridBot.Extended;

/// <summary>
/// Configuration options for the Extended WebSocket client.
/// </summary>
public sealed class WebSocketOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ExtendedWebSocket";

    /// <summary>
    /// WebSocket URL. Auto-derived from ApiUrl if not set.
    /// Mainnet: wss://api.starknet.extended.exchange/stream.extended.exchange/v1
    /// Testnet: wss://starknet.sepolia.extended.exchange/stream.extended.exchange/v1
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
    /// Heartbeat interval in seconds (ping/pong).
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Heartbeat timeout in seconds. Connection considered dead if no pong received.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 10;

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
    public int MaxReconnectAttempts { get; set; } = int.MaxValue; // Unlimited with backoff

    /// <summary>
    /// Data staleness threshold in seconds. Orders halted if data older than this.
    /// </summary>
    public int DataStalenessThresholdSeconds { get; set; } = 5;

    /// <summary>
    /// Number of disconnections within DisconnectionAlertWindowMinutes that triggers an alert.
    /// </summary>
    public int DisconnectionAlertThreshold { get; set; } = 5;

    /// <summary>
    /// Time window in minutes for counting disconnections for alert threshold.
    /// </summary>
    public int DisconnectionAlertWindowMinutes { get; set; } = 5;
}
