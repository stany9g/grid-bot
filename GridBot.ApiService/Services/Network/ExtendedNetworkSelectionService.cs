using GridBot.Abstractions.Factory;
using GridBot.ApiService.Services.Bot;
using GridBot.Extended;
using GridBot.Extended.Factory;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Thread-safe service for managing Extended DEX network selection.
/// Validates that the bot is stopped before allowing network switches.
/// When a network is selected, the exchange client is initialized for that network.
/// </summary>
public sealed class ExtendedNetworkSelectionService : IExtendedNetworkSelectionService
{
    private readonly ExtendedNetworksOptions _networksOptions;
    private readonly IExtendedNetworkExchangeFactory _exchangeFactory;
    private readonly IExchangeRegistry _exchangeRegistry;
    private readonly IGridBotControlService _botControlService;
    private readonly ILogger<ExtendedNetworkSelectionService> _logger;
    private readonly object _lock = new();

    private ExtendedNetworkType _currentNetwork;
    private List<ExtendedNetworkInfo> _availableNetworks = [];
    private volatile bool _isSwitchingNetwork;

    public ExtendedNetworkSelectionService(
        IOptions<ExtendedNetworksOptions> networksOptions,
        IExtendedNetworkExchangeFactory exchangeFactory,
        IExchangeRegistry exchangeRegistry,
        IGridBotControlService botControlService,
        ILogger<ExtendedNetworkSelectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(networksOptions);
        ArgumentNullException.ThrowIfNull(exchangeFactory);
        ArgumentNullException.ThrowIfNull(exchangeRegistry);
        ArgumentNullException.ThrowIfNull(botControlService);
        ArgumentNullException.ThrowIfNull(logger);

        _networksOptions = networksOptions.Value;
        _exchangeFactory = exchangeFactory;
        _exchangeRegistry = exchangeRegistry;
        _botControlService = botControlService;
        _logger = logger;

        InitializeFromConfiguration();
    }

    public ExtendedNetworkType CurrentNetwork
    {
        get
        {
            lock (_lock)
            {
                return _currentNetwork;
            }
        }
    }

    public IReadOnlyList<ExtendedNetworkInfo> AvailableNetworks
    {
        get
        {
            lock (_lock)
            {
                return _availableNetworks.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Gets whether a network switch is currently in progress.
    /// </summary>
    public bool IsSwitchingNetwork => _isSwitchingNetwork;

    public event EventHandler<ExtendedNetworkType>? NetworkChanged;

    public async Task SelectNetworkAsync(ExtendedNetworkType network, CancellationToken ct = default)
    {
        bool networkChanged = false;
        ExtendedNetworkType previousNetwork;

        try
        {
            _isSwitchingNetwork = true;

            lock (_lock)
            {
                if (_botControlService.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Cannot switch network while bot is running. Stop the bot first.");
                }

                if (!_networksOptions.IsNetworkConfigured(network))
                {
                    throw new ArgumentException(
                        $"Network {network} is not configured. Please add valid credentials in appsettings.",
                        nameof(network));
                }

                if (_currentNetwork == network && _exchangeFactory.IsInitialized(network))
                {
                    _logger.LogDebug("Network {Network} is already selected and initialized", network);
                    return;
                }

                previousNetwork = _currentNetwork;
            }

            // Dispose the current client if switching to a different network
            var currentFactoryNetwork = _exchangeFactory.GetCurrentNetwork();
            if (currentFactoryNetwork.HasValue && currentFactoryNetwork.Value != network)
            {
                _logger.LogInformation(
                    "Disposing current exchange client for network {Network}",
                    currentFactoryNetwork.Value);

                // Unregister from registry before disposing
                var currentClient = _exchangeFactory.GetCurrent();
                if (currentClient is not null)
                {
                    _exchangeRegistry.Unregister(currentClient.ExchangeId);
                }

                await _exchangeFactory.DisposeCurrentAsync();
            }

            // Create the new exchange client for the selected network
            _logger.LogInformation("Creating exchange client for network {Network}", network);
            var newClient = await _exchangeFactory.CreateForNetworkAsync(network, ct);

            // Register the new client in the registry
            _exchangeRegistry.Register(newClient);

            // Set as primary so all services use this exchange
            _exchangeRegistry.SetPrimary(newClient.ExchangeId);

            lock (_lock)
            {
                _currentNetwork = network;
                networkChanged = true;

                _logger.LogInformation(
                    "Network switched from {PreviousNetwork} to {NewNetwork} (ExchangeId: {ExchangeId})",
                    previousNetwork,
                    network,
                    newClient.ExchangeId);
            }

            // Fire event outside lock to prevent deadlock
            if (networkChanged)
            {
                NetworkChanged?.Invoke(this, network);
            }
        }
        finally
        {
            _isSwitchingNetwork = false;
        }
    }

    public Task RefreshNetworkStatusAsync(CancellationToken ct = default)
    {
        var networks = new List<ExtendedNetworkInfo>();

        foreach (var networkType in Enum.GetValues<ExtendedNetworkType>())
        {
            var isConfigured = _networksOptions.IsNetworkConfigured(networkType);
            var displayName = GetDisplayName(networkType);
            var statusMessage = GetStatusMessage(networkType, isConfigured);

            networks.Add(new ExtendedNetworkInfo(
                networkType,
                displayName,
                isConfigured,
                statusMessage));
        }

        lock (_lock)
        {
            _availableNetworks = networks;
        }

        var configuredCount = networks.Count(n => n.IsConfigured);
        var totalCount = networks.Count;

        _logger.LogDebug(
            "Refreshed Extended network status: {ConfiguredCount} configured, {TotalCount} total",
            configuredCount,
            totalCount);

        return Task.CompletedTask;
    }

    private void InitializeFromConfiguration()
    {
        var defaultNetwork = _networksOptions.GetDefaultNetworkType();

        if (_networksOptions.IsNetworkConfigured(defaultNetwork))
        {
            _currentNetwork = defaultNetwork;
            _logger.LogInformation("Extended initialized with default network: {Network}", defaultNetwork);
        }
        else
        {
            // Try to find any configured network as fallback
            foreach (var networkType in Enum.GetValues<ExtendedNetworkType>())
            {
                if (_networksOptions.IsNetworkConfigured(networkType))
                {
                    _currentNetwork = networkType;
                    _logger.LogWarning(
                        "Extended default network {DefaultNetwork} is not configured, using {FallbackNetwork} instead",
                        defaultNetwork,
                        networkType);
                    break;
                }
            }

            // If no network is configured at all, default to Testnet
            if (!_networksOptions.IsNetworkConfigured(_currentNetwork))
            {
                _currentNetwork = ExtendedNetworkType.Testnet;
                _logger.LogWarning("No Extended network is configured, defaulting to Testnet");
            }
        }

        _ = RefreshNetworkStatusAsync();
    }

    private static string GetDisplayName(ExtendedNetworkType network) => network switch
    {
        ExtendedNetworkType.Testnet => "Extended Testnet",
        ExtendedNetworkType.Mainnet => "Extended Mainnet",
        _ => network.ToString()
    };

    private string GetStatusMessage(ExtendedNetworkType network, bool isConfigured)
    {
        if (!isConfigured)
        {
            return "Not Configured";
        }

        var options = _networksOptions.GetNetwork(network);
        if (options is null)
        {
            return "Not Configured";
        }

        // Check if API URL matches the expected network
        var isTestnetUrl = options.ApiUrl.Contains("sepolia", StringComparison.OrdinalIgnoreCase);
        var expectedTestnet = network == ExtendedNetworkType.Testnet;

        if (isTestnetUrl != expectedTestnet)
        {
            return $"URL mismatch: expected {(expectedTestnet ? "testnet" : "mainnet")} URL";
        }

        return "Ready";
    }
}
