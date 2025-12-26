using GridBot.Abstractions.Factory;
using GridBot.ApiService.Services.Bot;
using GridBot.Lighter;
using GridBot.Lighter.Factory;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Thread-safe service for managing Lighter network selection.
/// Validates that the bot is stopped before allowing network switches.
/// When a network is selected, the exchange client is initialized for that network.
/// </summary>
public sealed class NetworkSelectionService : INetworkSelectionService
{
    private readonly LighterNetworksOptions _networksOptions;
    private readonly INetworkExchangeFactory _exchangeFactory;
    private readonly IExchangeRegistry _exchangeRegistry;
    private readonly IGridBotControlService _botControlService;
    private readonly ILogger<NetworkSelectionService> _logger;
    private readonly object _lock = new();

    private LighterNetworkType _currentNetwork;
    private List<NetworkInfo> _availableNetworks = [];
    private volatile bool _isSwitchingNetwork;

    public NetworkSelectionService(
        IOptions<LighterNetworksOptions> networksOptions,
        INetworkExchangeFactory exchangeFactory,
        IExchangeRegistry exchangeRegistry,
        IGridBotControlService botControlService,
        ILogger<NetworkSelectionService> logger)
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

    public LighterNetworkType CurrentNetwork
    {
        get
        {
            lock (_lock)
            {
                return _currentNetwork;
            }
        }
    }

    public IReadOnlyList<NetworkInfo> AvailableNetworks
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

    public event EventHandler<LighterNetworkType>? NetworkChanged;

    public async Task SelectNetworkAsync(LighterNetworkType network, CancellationToken ct = default)
    {
        bool networkChanged = false;
        LighterNetworkType previousNetwork;

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
        var networks = new List<NetworkInfo>();

        foreach (var networkType in Enum.GetValues<LighterNetworkType>())
        {
            var isConfigured = _networksOptions.IsNetworkConfigured(networkType);
            var displayName = GetDisplayName(networkType);
            var statusMessage = GetStatusMessage(networkType, isConfigured);

            networks.Add(new NetworkInfo(
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
            "Refreshed network status: {ConfiguredCount} configured, {TotalCount} total",
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
            _logger.LogInformation("Initialized with default network: {Network}", defaultNetwork);
        }
        else
        {
            // Try to find any configured network as fallback
            foreach (var networkType in Enum.GetValues<LighterNetworkType>())
            {
                if (_networksOptions.IsNetworkConfigured(networkType))
                {
                    _currentNetwork = networkType;
                    _logger.LogWarning(
                        "Default network {DefaultNetwork} is not configured, using {FallbackNetwork} instead",
                        defaultNetwork,
                        networkType);
                    break;
                }
            }

            // If no network is configured at all, default to Testnet
            if (!_networksOptions.IsNetworkConfigured(_currentNetwork))
            {
                _currentNetwork = LighterNetworkType.Testnet;
                _logger.LogWarning("No network is configured, defaulting to Testnet");
            }
        }

        _ = RefreshNetworkStatusAsync();
    }

    private static string GetDisplayName(LighterNetworkType network) => network switch
    {
        LighterNetworkType.Testnet => "Lighter Testnet",
        LighterNetworkType.Mainnet => "Lighter Mainnet",
        _ => network.ToString()
    };

    private string GetStatusMessage(LighterNetworkType network, bool isConfigured)
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

        var chainId = options.ChainId;
        var expectedChainId = network switch
        {
            LighterNetworkType.Testnet => 300,
            LighterNetworkType.Mainnet => 304,
            _ => 0
        };

        if (chainId != expectedChainId)
        {
            return $"Chain ID mismatch: expected {expectedChainId}, got {chainId}";
        }

        return "Ready";
    }
}
