using GridBot.Abstractions.Communication;
using Microsoft.Extensions.Logging;
using WsConnectionState = GridBot.Extended.Models.WebSocket.ConnectionState;
using WsConnectionStateEvent = GridBot.Extended.Models.WebSocket.ConnectionStateEvent;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Adapts Extended WebSocket client to <see cref="IExchangeConnection"/>.
/// </summary>
internal sealed class ExtendedConnectionAdapter : IExchangeConnection
{
    private readonly string _exchangeId;
    private readonly IExtendedWebSocketClient _wsClient;
    private readonly IExtendedHttpClient _httpClient;
    private readonly ExtendedMarketMapper _marketMapper;
    private readonly NonceManager _nonceManager;
    private readonly ExtendedRealtimeAdapter _realtimeAdapter;
    private readonly ILogger<ExtendedConnectionAdapter> _logger;
    private int _disconnectCount24h;
    private DateTimeOffset _lastDisconnectCountReset = DateTimeOffset.UtcNow;
    private ConnectionState _previousState = ConnectionState.Disconnected;
    private CancellationTokenSource? _connectionMonitorCts;
    private Task? _connectionMonitorTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedConnectionAdapter"/> class.
    /// </summary>
    public ExtendedConnectionAdapter(
        string exchangeId,
        IExtendedWebSocketClient wsClient,
        IExtendedHttpClient httpClient,
        ExtendedMarketMapper marketMapper,
        NonceManager nonceManager,
        ExtendedRealtimeAdapter realtimeAdapter,
        ILogger<ExtendedConnectionAdapter> logger)
    {
        _exchangeId = exchangeId ?? throw new ArgumentNullException(nameof(exchangeId));
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _nonceManager = nonceManager ?? throw new ArgumentNullException(nameof(nonceManager));
        _realtimeAdapter = realtimeAdapter ?? throw new ArgumentNullException(nameof(realtimeAdapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ExchangeId => _exchangeId;

    /// <inheritdoc />
    public ConnectionState State => MapConnectionState(_wsClient.ConnectionState);

    /// <inheritdoc />
    public bool IsHealthy => _wsClient.IsConnected && !_realtimeAdapter.IsDataStale;

    /// <inheritdoc />
    public TimeSpan? DataAge => _realtimeAdapter.DataAge;

    /// <inheritdoc />
    public int DisconnectCount24h
    {
        get
        {
            ResetDisconnectCountIfNeeded();
            return _disconnectCount24h;
        }
    }

    /// <inheritdoc />
    public event EventHandler<ConnectionHealthEventArgs>? HealthChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting to Extended exchange {ExchangeId}...", _exchangeId);

        try
        {
            // Step 1: Initialize market mapper
            await _marketMapper.InitializeAsync(_httpClient, ct);

            // Step 2: Ensure nonce manager is initialized (uses InitialNonce from config)
            // Note: Extended API doesn't return nonce in account info, so we rely on config
            if (!_nonceManager.IsInitialized)
            {
                _logger.LogInformation("NonceManager using initial nonce from configuration");
                _nonceManager.SyncFromServer(0); // Will be overwritten by config value if set
            }

            // Step 3: Connect WebSocket
            await _wsClient.ConnectAsync(ct);

            // Step 4: Start connection state monitoring
            StartConnectionMonitoring();

            // Step 5: Subscribe to account stream
            await _wsClient.SubscribeAccountAsync(ct);

            // Step 6: Wait for initial data
            await Task.Delay(500, ct); // Brief wait for initial snapshot

            // Step 7: Start realtime processing
            _realtimeAdapter.StartProcessing();

            // Step 8: Reconcile state
            await _realtimeAdapter.ReconcileStateAsync(ct);

            _logger.LogInformation("Connected to Extended exchange {ExchangeId}", _exchangeId);

            OnHealthChanged(ConnectionState.Connected, "Connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Extended exchange {ExchangeId}", _exchangeId);
            OnHealthChanged(ConnectionState.Failed, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Disconnecting from Extended exchange {ExchangeId}...", _exchangeId);

        await StopConnectionMonitoringAsync();
        await _realtimeAdapter.StopProcessingAsync();
        await _wsClient.DisconnectAsync(ct);

        _logger.LogInformation("Disconnected from Extended exchange {ExchangeId}", _exchangeId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopConnectionMonitoringAsync();
        await _realtimeAdapter.StopProcessingAsync();
        await _wsClient.DisposeAsync();
    }

    private void StartConnectionMonitoring()
    {
        _connectionMonitorCts = new CancellationTokenSource();
        _connectionMonitorTask = Task.Run(() => MonitorConnectionStateAsync(_connectionMonitorCts.Token));
    }

    private async Task StopConnectionMonitoringAsync()
    {
        if (_connectionMonitorCts != null)
        {
            await _connectionMonitorCts.CancelAsync();
            if (_connectionMonitorTask != null)
            {
                try
                {
                    await _connectionMonitorTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
            _connectionMonitorCts.Dispose();
            _connectionMonitorCts = null;
        }
    }

    private async Task MonitorConnectionStateAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var stateEvent in _wsClient.ConnectionStateChanges.ReadAllAsync(ct))
            {
                HandleConnectionStateChange(stateEvent);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected
        }
    }

    private void HandleConnectionStateChange(WsConnectionStateEvent stateEvent)
    {
        var mappedState = MapConnectionState(stateEvent.State);

        if (stateEvent.State == WsConnectionState.Disconnected)
        {
            HandleDisconnected();
        }
        else if (stateEvent.State == WsConnectionState.Connected &&
                 _previousState != ConnectionState.Connected)
        {
            _ = HandleReconnectedAsync();
        }

        OnHealthChanged(mappedState, stateEvent.Reason);
    }

    private void HandleDisconnected()
    {
        ResetDisconnectCountIfNeeded();
        _disconnectCount24h++;

        _logger.LogWarning(
            "WebSocket disconnected for {ExchangeId}. Disconnects in 24h: {Count}",
            _exchangeId, _disconnectCount24h);

        // Check if too many disconnects
        if (_disconnectCount24h >= 20)
        {
            _logger.LogWarning(
                "High disconnect rate for {ExchangeId}: {Count} in 24h. Consider switching to REST-only mode.",
                _exchangeId, _disconnectCount24h);
        }
    }

    private async Task HandleReconnectedAsync()
    {
        _logger.LogInformation("WebSocket reconnected for {ExchangeId}", _exchangeId);

        try
        {
            // Re-subscribe to account stream
            await _wsClient.SubscribeAccountAsync(default);

            // Reconcile state via REST
            await _realtimeAdapter.ReconcileStateAsync(default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile state after reconnection");
            OnHealthChanged(ConnectionState.Failed, $"Reconciliation failed: {ex.Message}");
        }
    }

    private void ResetDisconnectCountIfNeeded()
    {
        if (DateTimeOffset.UtcNow - _lastDisconnectCountReset > TimeSpan.FromHours(24))
        {
            _disconnectCount24h = 0;
            _lastDisconnectCountReset = DateTimeOffset.UtcNow;
        }
    }

    private void OnHealthChanged(ConnectionState newState, string? reason = null)
    {
        var args = new ConnectionHealthEventArgs
        {
            PreviousState = _previousState,
            CurrentState = newState,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        };

        _previousState = newState;
        HealthChanged?.Invoke(this, args);
    }

    private static ConnectionState MapConnectionState(WsConnectionState state)
    {
        return state switch
        {
            WsConnectionState.Disconnected => ConnectionState.Disconnected,
            WsConnectionState.Connecting => ConnectionState.Connecting,
            WsConnectionState.Connected => ConnectionState.Connected,
            WsConnectionState.Reconnecting => ConnectionState.Reconnecting,
            WsConnectionState.Failed => ConnectionState.Failed,
            _ => ConnectionState.Disconnected
        };
    }
}
