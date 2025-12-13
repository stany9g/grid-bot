namespace GridBot.ApiService.Services.Connectivity;

/// <summary>
/// Monitors WebSocket health and detects connection issues that could affect trading safety.
/// Implements H.2 CRITICAL: WebSocket Health Monitoring specification.
/// </summary>
public interface IWebSocketHealthMonitor
{
    /// <summary>
    /// Whether WebSocket is healthy for trading.
    /// Requires: connected, data age less than threshold, no excessive reconnect cycles.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Reason if not healthy, null if healthy.
    /// </summary>
    string? UnhealthyReason { get; }

    /// <summary>
    /// Whether grid operations should be paused due to WebSocket issues.
    /// </summary>
    bool ShouldPauseGrid { get; }

    /// <summary>
    /// Whether the system should enter protective mode due to extended outage.
    /// </summary>
    bool ShouldEnterProtectiveMode { get; }

    /// <summary>
    /// Time since the WebSocket was last in a healthy state.
    /// Null if currently healthy or never been healthy.
    /// </summary>
    TimeSpan? TimeSinceHealthy { get; }

    /// <summary>
    /// Number of reconnect cycles detected in the last 5 minutes.
    /// 3+ cycles triggers a 10-minute pause.
    /// </summary>
    int RecentReconnectCycles { get; }

    /// <summary>
    /// Whether reconnect cycling pause is active.
    /// </summary>
    bool IsReconnectCyclePauseActive { get; }

    /// <summary>
    /// Time remaining on reconnect cycle pause.
    /// Null if pause is not active.
    /// </summary>
    TimeSpan? ReconnectCyclePauseRemaining { get; }

    /// <summary>
    /// Check current health status and get a detailed snapshot.
    /// </summary>
    WebSocketHealthStatus CheckHealth();
}

/// <summary>
/// WebSocket health status snapshot with all relevant metrics.
/// </summary>
public sealed record WebSocketHealthStatus
{
    /// <summary>
    /// Whether WebSocket is healthy for trading.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Whether the WebSocket is currently connected.
    /// </summary>
    public required bool IsConnected { get; init; }

    /// <summary>
    /// Age of the most recent data received.
    /// </summary>
    public TimeSpan? DataAge { get; init; }

    /// <summary>
    /// Time since the last message was received on any channel.
    /// </summary>
    public TimeSpan? TimeSinceLastMessage { get; init; }

    /// <summary>
    /// Number of disconnect events in the last 24 hours.
    /// </summary>
    public required int DisconnectCount24h { get; init; }

    /// <summary>
    /// Number of reconnect cycles (disconnect + reconnect) in the last 5 minutes.
    /// </summary>
    public required int RecentReconnectCycles { get; init; }

    /// <summary>
    /// Reason if not healthy, null if healthy.
    /// </summary>
    public string? UnhealthyReason { get; init; }

    /// <summary>
    /// Whether grid operations should be paused.
    /// </summary>
    public required bool ShouldPauseGrid { get; init; }

    /// <summary>
    /// Whether the system should enter protective mode.
    /// </summary>
    public required bool ShouldEnterProtectiveMode { get; init; }

    /// <summary>
    /// Whether silence detection (30s no messages) is triggered.
    /// </summary>
    public required bool IsSilenceDetected { get; init; }

    /// <summary>
    /// Whether reconnect cycle pause (3 cycles in 5 min) is active.
    /// </summary>
    public required bool IsReconnectCyclePauseActive { get; init; }

    /// <summary>
    /// Time remaining on reconnect cycle pause.
    /// </summary>
    public TimeSpan? ReconnectCyclePauseRemaining { get; init; }

    /// <summary>
    /// When this status snapshot was created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}
