using GridBot.Abstractions.Factory;
using GridBot.ApiService.Services.Bot;
using GridBot.ApiService.Services.Network;
using GridBot.Lighter;

namespace GridBot.ApiService.Services.Exchange;

/// <summary>
/// Service for managing exchange selection and switching.
/// Thread-safe implementation that validates bot state before switching.
/// Subscribes to network changes to update the current client automatically.
/// </summary>
public sealed class ExchangeSelectionService : IExchangeSelectionService, IDisposable
{
    private readonly IExchangeRegistry _exchangeRegistry;
    private readonly INetworkSelectionService _networkSelectionService;
    private readonly IGridBotControlService _botControlService;
    private readonly ILogger<ExchangeSelectionService> _logger;
    private readonly object _lock = new();

    private ExchangeType? _currentExchangeType;
    private IExchangeClient? _currentClient;
    private List<ExchangeInfo> _availableExchanges = [];
    private bool _disposed;

    public ExchangeSelectionService(
        IExchangeRegistry exchangeRegistry,
        INetworkSelectionService networkSelectionService,
        IGridBotControlService botControlService,
        ILogger<ExchangeSelectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(exchangeRegistry);
        ArgumentNullException.ThrowIfNull(networkSelectionService);
        ArgumentNullException.ThrowIfNull(botControlService);
        ArgumentNullException.ThrowIfNull(logger);

        _exchangeRegistry = exchangeRegistry;
        _networkSelectionService = networkSelectionService;
        _botControlService = botControlService;
        _logger = logger;

        // Subscribe to network changes
        _networkSelectionService.NetworkChanged += OnNetworkChanged;

        InitializeFromRegistry();
    }

    public ExchangeType? CurrentExchangeType
    {
        get
        {
            lock (_lock)
            {
                return _currentExchangeType;
            }
        }
    }

    public IExchangeClient? CurrentClient
    {
        get
        {
            lock (_lock)
            {
                return _currentClient;
            }
        }
    }

    public IReadOnlyList<ExchangeInfo> AvailableExchanges
    {
        get
        {
            lock (_lock)
            {
                return _availableExchanges.AsReadOnly();
            }
        }
    }

    public event EventHandler<ExchangeType>? ExchangeChanged;

    public Task SelectExchangeAsync(ExchangeType exchangeType, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_botControlService.IsRunning)
            {
                throw new InvalidOperationException("Cannot switch exchange while bot is running. Stop the bot first.");
            }

            var clients = _exchangeRegistry.GetByType(exchangeType);
            if (clients.Count == 0)
            {
                throw new ArgumentException($"Exchange type {exchangeType} is not available", nameof(exchangeType));
            }

            var client = clients[0];
            if (!client.Connection.IsHealthy)
            {
                _logger.LogWarning("Selected exchange {ExchangeType} is not healthy, proceeding anyway", exchangeType);
            }

            // Update the registry's primary exchange so all services use the correct client
            _exchangeRegistry.SetPrimary(client.ExchangeId);

            _currentExchangeType = exchangeType;
            _currentClient = client;

            _logger.LogInformation("Selected exchange: {ExchangeType} (ID: {ExchangeId})", exchangeType, client.ExchangeId);
        }

        ExchangeChanged?.Invoke(this, exchangeType);
        return Task.CompletedTask;
    }

    public Task RefreshAvailableExchangesAsync(CancellationToken ct = default)
    {
        var allClients = _exchangeRegistry.GetAll();
        var exchanges = new List<ExchangeInfo>();

        foreach (var client in allClients)
        {
            var displayName = GetDisplayName(client.ExchangeType);
            var isHealthy = client.Connection.IsHealthy;
            var statusMessage = isHealthy ? "Connected" : GetStatusMessage(client);

            exchanges.Add(new ExchangeInfo(
                client.ExchangeType,
                displayName,
                IsAvailable: true,
                statusMessage));
        }

        // Add entries for known exchange types that are not configured
        foreach (ExchangeType type in Enum.GetValues<ExchangeType>())
        {
            if (!exchanges.Exists(e => e.Type == type))
            {
                exchanges.Add(new ExchangeInfo(
                    type,
                    GetDisplayName(type),
                    IsAvailable: false,
                    StatusMessage: "Not configured"));
            }
        }

        lock (_lock)
        {
            _availableExchanges = exchanges;
        }

        _logger.LogDebug("Refreshed available exchanges: {Count} total", exchanges.Count);
        return Task.CompletedTask;
    }

    private void OnNetworkChanged(object? sender, LighterNetworkType network)
    {
        if (_disposed) return;

        _logger.LogDebug("Network changed to {Network}, updating current exchange client", network);

        // When network changes, the registry should have the new client registered
        // Re-initialize from registry to pick up the new client
        InitializeFromRegistry();

        // Fire the ExchangeChanged event since the underlying client changed
        if (_currentExchangeType.HasValue)
        {
            ExchangeChanged?.Invoke(this, _currentExchangeType.Value);
        }
    }

    private void InitializeFromRegistry()
    {
        try
        {
            var primary = _exchangeRegistry.GetPrimary();
            lock (_lock)
            {
                _currentExchangeType = primary.ExchangeType;
                _currentClient = primary;
            }
            _logger.LogInformation("Initialized with primary exchange: {ExchangeType} (ID: {ExchangeId})", primary.ExchangeType, primary.ExchangeId);
        }
        catch (InvalidOperationException)
        {
            lock (_lock)
            {
                _currentExchangeType = null;
                _currentClient = null;
            }
            _logger.LogWarning("No exchange registered, selection service initialized without a current exchange");
        }

        _ = RefreshAvailableExchangesAsync();
    }

    private static string GetDisplayName(ExchangeType type) => type switch
    {
        ExchangeType.Lighter => "Lighter DEX",
        ExchangeType.Hyperliquid => "Hyperliquid",
        ExchangeType.Extended => "Extended DEX",
        _ => type.ToString()
    };

    private static string GetStatusMessage(IExchangeClient client)
    {
        var state = client.Connection.State;
        return state.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _networkSelectionService.NetworkChanged -= OnNetworkChanged;
    }
}
