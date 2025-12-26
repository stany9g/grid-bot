using GridBot.Abstractions.Factory;
using GridBot.ApiService.Services.Bot;

namespace GridBot.ApiService.Services.Exchange;

/// <summary>
/// Service for managing exchange selection and switching.
/// Thread-safe implementation that validates bot state before switching.
/// </summary>
public sealed class ExchangeSelectionService : IExchangeSelectionService
{
    private readonly IExchangeRegistry _exchangeRegistry;
    private readonly IGridBotControlService _botControlService;
    private readonly ILogger<ExchangeSelectionService> _logger;
    private readonly object _lock = new();

    private ExchangeType? _currentExchangeType;
    private IExchangeClient? _currentClient;
    private List<ExchangeInfo> _availableExchanges = [];

    public ExchangeSelectionService(
        IExchangeRegistry exchangeRegistry,
        IGridBotControlService botControlService,
        ILogger<ExchangeSelectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(exchangeRegistry);
        ArgumentNullException.ThrowIfNull(botControlService);
        ArgumentNullException.ThrowIfNull(logger);

        _exchangeRegistry = exchangeRegistry;
        _botControlService = botControlService;
        _logger = logger;

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

    private void InitializeFromRegistry()
    {
        try
        {
            var primary = _exchangeRegistry.GetPrimary();
            _currentExchangeType = primary.ExchangeType;
            _currentClient = primary;
            _logger.LogInformation("Initialized with primary exchange: {ExchangeType}", primary.ExchangeType);
        }
        catch (InvalidOperationException)
        {
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
}
