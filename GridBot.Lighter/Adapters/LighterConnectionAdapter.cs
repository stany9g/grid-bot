using GridBot.Abstractions.Communication;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;
using ConnectionStateAbstraction = GridBot.Abstractions.Communication.ConnectionState;
using ConnectionStateLighter = GridBot.Lighter.Models.WebSocket.ConnectionState;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="ILighterWebSocketClient"/> and <see cref="ILighterRealtimeState"/>
/// to the <see cref="IExchangeConnection"/> interface.
/// </summary>
internal sealed class LighterConnectionAdapter : IExchangeConnection
{
    private readonly ILighterWebSocketClient _wsClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly string _exchangeId;
    private readonly ILogger<LighterConnectionAdapter> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterConnectionAdapter"/> class.
    /// </summary>
    /// <param name="wsClient">The WebSocket client.</param>
    /// <param name="realtimeState">The realtime state service.</param>
    /// <param name="exchangeId">The exchange identifier.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterConnectionAdapter(
        ILighterWebSocketClient wsClient,
        ILighterRealtimeState realtimeState,
        string exchangeId,
        ILogger<LighterConnectionAdapter> logger)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _exchangeId = exchangeId ?? throw new ArgumentNullException(nameof(exchangeId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to health changes from the realtime state service
        _realtimeState.HealthChanged += OnRealtimeHealthChanged;
    }

    /// <inheritdoc />
    public string ExchangeId => _exchangeId;

    /// <inheritdoc />
    public ConnectionStateAbstraction State => MapConnectionState(_wsClient.ConnectionState);

    /// <inheritdoc />
    public bool IsHealthy => _realtimeState.IsConnected && _realtimeState.TimeSinceLastMessage?.TotalSeconds < 30;

    /// <inheritdoc />
    public TimeSpan? DataAge => _realtimeState.OldestDataAge;

    /// <inheritdoc />
    public int DisconnectCount24h => _realtimeState.DisconnectCount24h;

    /// <inheritdoc />
    public event EventHandler<ConnectionHealthEventArgs>? HealthChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Connecting to Lighter exchange {ExchangeId}", _exchangeId);

        // Initialize the realtime state service which handles WebSocket connection
        await _realtimeState.InitializeAsync(ct);

        _logger.LogInformation("Connected to Lighter exchange {ExchangeId}", _exchangeId);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Disconnecting from Lighter exchange {ExchangeId}", _exchangeId);

        await _wsClient.DisconnectAsync(ct);

        _logger.LogInformation("Disconnected from Lighter exchange {ExchangeId}", _exchangeId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _realtimeState.HealthChanged -= OnRealtimeHealthChanged;

        await _realtimeState.DisposeAsync();

        _logger.LogDebug("LighterConnectionAdapter disposed");
    }

    private void OnRealtimeHealthChanged(object? sender, WebSocketHealthChangedEventArgs e)
    {
        var previousState = e.IsConnectionEvent && !e.IsHealthy
            ? ConnectionStateAbstraction.Connected
            : ConnectionStateAbstraction.Disconnected;

        var currentState = e.IsConnected
            ? ConnectionStateAbstraction.Connected
            : e.IsHealthy ? ConnectionStateAbstraction.Reconnecting : ConnectionStateAbstraction.Disconnected;

        var args = new ConnectionHealthEventArgs
        {
            PreviousState = previousState,
            CurrentState = currentState,
            Reason = e.Reason,
            Timestamp = e.Timestamp
        };

        HealthChanged?.Invoke(this, args);
    }

    private static ConnectionStateAbstraction MapConnectionState(ConnectionStateLighter state)
    {
        return state switch
        {
            ConnectionStateLighter.Disconnected => ConnectionStateAbstraction.Disconnected,
            ConnectionStateLighter.Connecting => ConnectionStateAbstraction.Connecting,
            ConnectionStateLighter.Connected => ConnectionStateAbstraction.Connected,
            ConnectionStateLighter.Reconnecting => ConnectionStateAbstraction.Reconnecting,
            ConnectionStateLighter.Failed => ConnectionStateAbstraction.Failed,
            _ => ConnectionStateAbstraction.Disconnected
        };
    }
}
