using GridBot.ApiService.Configuration;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Connectivity;

/// <summary>
/// Monitors WebSocket health and detects connection issues that could affect trading safety.
/// Implements H.2 CRITICAL: WebSocket Health Monitoring specification.
///
/// Rules:
/// 1. IF websocket_disconnected THEN pause_grid_immediately
/// 2. IF websocket_reconnected AND data_age less than 10s THEN resume_grid
/// 3. IF websocket_disconnected greater than 5_minutes THEN enter_protective_mode
/// 4. IF reconnect_cycles_in_5min >= 3 THEN pause_for_10_minutes
/// </summary>
public sealed class WebSocketHealthMonitor : IWebSocketHealthMonitor, IDisposable
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly DecisionEngineOptions _options;
    private readonly ILogger<WebSocketHealthMonitor> _logger;

    // State tracking
    private readonly List<DateTimeOffset> _reconnectEvents = new();
    private readonly object _reconnectLock = new();
    private DateTimeOffset? _lastHealthyTime;
    private DateTimeOffset? _disconnectedSince;
    private DateTimeOffset? _reconnectCyclePauseUntil;
    private bool _wasHealthy;
    private bool _disposed;

    public WebSocketHealthMonitor(
        ILighterRealtimeState realtimeState,
        IOptions<TradingBotOptions> options,
        ILogger<WebSocketHealthMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(realtimeState);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _realtimeState = realtimeState;
        _options = options.Value.DecisionEngine;
        _logger = logger;

        // Subscribe to health change events
        _realtimeState.HealthChanged += OnHealthChanged;

        _logger.LogInformation(
            "WebSocketHealthMonitor initialized. Thresholds: MaxDataAge={MaxAge}s, Silence={Silence}s, " +
            "ExtendedOutage={Outage}min, MaxReconnectCycles={Cycles}/5min, CyclePause={Pause}min",
            _options.MaxWebSocketDataAgeSeconds,
            _options.SilenceDetectionSeconds,
            _options.ExtendedOutageMinutes,
            _options.MaxReconnectCyclesIn5Min,
            _options.ReconnectCyclePauseMinutes);
    }

    /// <inheritdoc />
    public bool IsHealthy
    {
        get
        {
            var status = CheckHealth();
            return status.IsHealthy;
        }
    }

    /// <inheritdoc />
    public string? UnhealthyReason
    {
        get
        {
            var status = CheckHealth();
            return status.UnhealthyReason;
        }
    }

    /// <inheritdoc />
    public bool ShouldPauseGrid
    {
        get
        {
            var status = CheckHealth();
            return status.ShouldPauseGrid;
        }
    }

    /// <inheritdoc />
    public bool ShouldEnterProtectiveMode
    {
        get
        {
            var status = CheckHealth();
            return status.ShouldEnterProtectiveMode;
        }
    }

    /// <inheritdoc />
    public TimeSpan? TimeSinceHealthy
    {
        get
        {
            if (_lastHealthyTime.HasValue && !IsHealthy)
            {
                return DateTimeOffset.UtcNow - _lastHealthyTime.Value;
            }
            return null;
        }
    }

    /// <inheritdoc />
    public int RecentReconnectCycles
    {
        get
        {
            lock (_reconnectLock)
            {
                var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
                _reconnectEvents.RemoveAll(e => e < cutoff);
                return _reconnectEvents.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool IsReconnectCyclePauseActive
    {
        get
        {
            return _reconnectCyclePauseUntil.HasValue &&
                   DateTimeOffset.UtcNow < _reconnectCyclePauseUntil.Value;
        }
    }

    /// <inheritdoc />
    public TimeSpan? ReconnectCyclePauseRemaining
    {
        get
        {
            if (!_reconnectCyclePauseUntil.HasValue)
                return null;

            var remaining = _reconnectCyclePauseUntil.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /// <inheritdoc />
    public WebSocketHealthStatus CheckHealth()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var now = DateTimeOffset.UtcNow;
        var isConnected = _realtimeState.IsConnected;
        var dataAge = _realtimeState.OldestDataAge;
        var timeSinceLastMessage = _realtimeState.TimeSinceLastMessage;
        var disconnectCount24h = _realtimeState.DisconnectCount24h;
        var recentCycles = RecentReconnectCycles;

        // Check silence detection (30s no messages = treat as disconnect)
        var isSilenceDetected = timeSinceLastMessage.HasValue &&
                                timeSinceLastMessage.Value.TotalSeconds >= _options.SilenceDetectionSeconds;

        // Check reconnect cycle pause
        var isReconnectPauseActive = IsReconnectCyclePauseActive;
        var pauseRemaining = ReconnectCyclePauseRemaining;

        // Determine health status
        string? unhealthyReason = null;
        var isHealthy = true;
        var shouldPauseGrid = false;
        var shouldEnterProtectiveMode = false;

        // Rule 1: IF websocket_disconnected THEN pause_grid_immediately
        if (!isConnected)
        {
            isHealthy = false;
            shouldPauseGrid = true;
            unhealthyReason = "WebSocket disconnected";

            // Track disconnect time for extended outage detection
            if (!_disconnectedSince.HasValue)
            {
                _disconnectedSince = now;
            }
        }

        // Rule: Silence detection (30s no messages = treat as disconnect)
        if (isSilenceDetected && isConnected)
        {
            isHealthy = false;
            shouldPauseGrid = true;
            unhealthyReason = $"WebSocket silent for {timeSinceLastMessage?.TotalSeconds:F0}s (threshold: {_options.SilenceDetectionSeconds}s)";

            if (!_disconnectedSince.HasValue)
            {
                _disconnectedSince = now;
            }
        }

        // Rule 2: IF websocket_reconnected AND data_age < 10s THEN resume_grid
        // (Inverse: if data age exceeds threshold, not healthy)
        if (isConnected && !isSilenceDetected && dataAge.HasValue &&
            dataAge.Value.TotalSeconds > _options.MaxWebSocketDataAgeSeconds)
        {
            isHealthy = false;
            shouldPauseGrid = true;
            unhealthyReason = $"WebSocket data stale ({dataAge.Value.TotalSeconds:F1}s > {_options.MaxWebSocketDataAgeSeconds}s threshold)";
        }

        // Rule 3: IF websocket_disconnected > 5_minutes THEN enter_protective_mode
        if (_disconnectedSince.HasValue)
        {
            var disconnectedDuration = now - _disconnectedSince.Value;
            if (disconnectedDuration.TotalMinutes >= _options.ExtendedOutageMinutes)
            {
                shouldEnterProtectiveMode = true;
                unhealthyReason = $"Extended outage: disconnected for {disconnectedDuration.TotalMinutes:F1} minutes";
            }
        }

        // Rule 4: IF reconnect_cycles_in_5min >= 3 THEN pause_for_10_minutes
        if (isReconnectPauseActive)
        {
            isHealthy = false;
            shouldPauseGrid = true;
            unhealthyReason = $"Reconnect cycle pause active ({pauseRemaining?.TotalMinutes:F1} minutes remaining)";
        }
        else if (recentCycles >= _options.MaxReconnectCyclesIn5Min)
        {
            // Trigger the pause
            _reconnectCyclePauseUntil = now.AddMinutes(_options.ReconnectCyclePauseMinutes);
            isHealthy = false;
            shouldPauseGrid = true;
            unhealthyReason = $"Too many reconnect cycles ({recentCycles} in 5 minutes) - pausing for {_options.ReconnectCyclePauseMinutes} minutes";

            _logger.LogWarning(
                "WS-HEALTH: Reconnect cycle limit reached ({Cycles}/{Max} in 5 min). Pausing for {Pause} minutes.",
                recentCycles, _options.MaxReconnectCyclesIn5Min, _options.ReconnectCyclePauseMinutes);
        }

        // Update tracking state
        if (isHealthy)
        {
            _lastHealthyTime = now;
            _disconnectedSince = null;
        }

        // Track health transitions for logging
        if (_wasHealthy && !isHealthy)
        {
            _logger.LogWarning(
                "WS-HEALTH: Transitioned to UNHEALTHY. Reason: {Reason}. " +
                "Connected={Connected}, DataAge={DataAge}s, TimeSinceMsg={TimeSinceMsg}s",
                unhealthyReason,
                isConnected,
                dataAge?.TotalSeconds.ToString("F1") ?? "N/A",
                timeSinceLastMessage?.TotalSeconds.ToString("F1") ?? "N/A");
        }
        else if (!_wasHealthy && isHealthy)
        {
            _logger.LogInformation(
                "WS-HEALTH: Transitioned to HEALTHY. DataAge={DataAge}s, TimeSinceMsg={TimeSinceMsg}s",
                dataAge?.TotalSeconds.ToString("F1") ?? "N/A",
                timeSinceLastMessage?.TotalSeconds.ToString("F1") ?? "N/A");
        }
        _wasHealthy = isHealthy;

        return new WebSocketHealthStatus
        {
            IsHealthy = isHealthy,
            IsConnected = isConnected,
            DataAge = dataAge,
            TimeSinceLastMessage = timeSinceLastMessage,
            DisconnectCount24h = disconnectCount24h,
            RecentReconnectCycles = recentCycles,
            UnhealthyReason = unhealthyReason,
            ShouldPauseGrid = shouldPauseGrid,
            ShouldEnterProtectiveMode = shouldEnterProtectiveMode,
            IsSilenceDetected = isSilenceDetected,
            IsReconnectCyclePauseActive = isReconnectPauseActive,
            ReconnectCyclePauseRemaining = pauseRemaining,
            Timestamp = now
        };
    }

    /// <summary>
    /// Handles health change events from the realtime state service.
    /// </summary>
    private void OnHealthChanged(object? sender, WebSocketHealthChangedEventArgs e)
    {
        if (e.IsConnectionEvent && e.IsConnected)
        {
            // Record reconnection event for cycle detection
            lock (_reconnectLock)
            {
                _reconnectEvents.Add(e.Timestamp);

                // Clean up old events
                var cutoff = e.Timestamp.AddMinutes(-5);
                _reconnectEvents.RemoveAll(ev => ev < cutoff);

                var recentCount = _reconnectEvents.Count;

                _logger.LogInformation(
                    "WS-HEALTH: WebSocket reconnected. Reconnect cycles in 5 min: {Count}/{Max}",
                    recentCount, _options.MaxReconnectCyclesIn5Min);

                // Check if we need to trigger pause
                if (recentCount >= _options.MaxReconnectCyclesIn5Min && !IsReconnectCyclePauseActive)
                {
                    _reconnectCyclePauseUntil = DateTimeOffset.UtcNow.AddMinutes(_options.ReconnectCyclePauseMinutes);

                    _logger.LogWarning(
                        "WS-HEALTH: Reconnect cycle limit reached ({Cycles}/{Max}). Pausing for {Pause} minutes.",
                        recentCount, _options.MaxReconnectCyclesIn5Min, _options.ReconnectCyclePauseMinutes);
                }
            }

            // Clear disconnect tracking on successful reconnection
            _disconnectedSince = null;
        }
        else if (e.IsConnectionEvent && !e.IsConnected)
        {
            // Track disconnect start time
            if (!_disconnectedSince.HasValue)
            {
                _disconnectedSince = e.Timestamp;
            }

            _logger.LogWarning(
                "WS-HEALTH: WebSocket disconnected. Reason: {Reason}",
                e.Reason);
        }
    }

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _realtimeState.HealthChanged -= OnHealthChanged;
    }
}
