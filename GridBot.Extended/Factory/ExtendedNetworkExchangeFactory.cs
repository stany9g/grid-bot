using GridBot.Abstractions.Factory;
using GridBot.Extended.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended.Factory;

/// <summary>
/// Factory that creates and manages Extended exchange clients for different networks.
/// Clients are created lazily and cached. Only one network can be active at a time.
/// </summary>
public sealed class ExtendedNetworkExchangeFactory : IExtendedNetworkExchangeFactory, IAsyncDisposable
{
    private readonly ExtendedNetworksOptions _networksOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ExtendedNetworkExchangeFactory> _logger;
    private readonly object _lock = new();

    private ExtendedNetworkType? _currentNetwork;
    private NetworkExchangeContext? _currentContext;
    private bool _disposed;

    public ExtendedNetworkExchangeFactory(
        IOptions<ExtendedNetworksOptions> networksOptions,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(networksOptions);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _networksOptions = networksOptions.Value;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ExtendedNetworkExchangeFactory>();
    }

    /// <inheritdoc />
    public async Task<IExchangeClient> CreateForNetworkAsync(ExtendedNetworkType network, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var networkOptions = _networksOptions.GetNetwork(network);
        if (networkOptions is null)
        {
            throw new InvalidOperationException($"Network {network} is not configured.");
        }

        var validationError = networkOptions.Validate();
        if (validationError is not null)
        {
            throw new InvalidOperationException($"Network {network} configuration is invalid: {validationError}");
        }

        lock (_lock)
        {
            // If we already have this network initialized, return the cached client
            if (_currentNetwork == network && _currentContext is not null)
            {
                _logger.LogDebug("Returning cached exchange client for network {Network}", network);
                return _currentContext.ExchangeClient;
            }
        }

        // Need to create a new context for this network
        _logger.LogInformation("Creating exchange client for network {Network}", network);

        // Create all the services for this network
        var context = await CreateNetworkContextAsync(network, networkOptions, ct);

        lock (_lock)
        {
            // Double-check in case another thread created it while we were initializing
            if (_currentNetwork == network && _currentContext is not null)
            {
                // Another thread beat us, dispose our new context and return theirs
                _ = context.DisposeAsync();
                return _currentContext.ExchangeClient;
            }

            // Store the new context
            _currentNetwork = network;
            _currentContext = context;
        }

        _logger.LogInformation(
            "Exchange client created for network {Network} (ExchangeId: {ExchangeId})",
            network,
            context.ExchangeClient.ExchangeId);

        return context.ExchangeClient;
    }

    /// <inheritdoc />
    public bool IsInitialized(ExtendedNetworkType network)
    {
        lock (_lock)
        {
            return _currentNetwork == network && _currentContext is not null;
        }
    }

    /// <inheritdoc />
    public IExchangeClient? GetCurrent()
    {
        lock (_lock)
        {
            return _currentContext?.ExchangeClient;
        }
    }

    /// <inheritdoc />
    public ExtendedNetworkType? GetCurrentNetwork()
    {
        lock (_lock)
        {
            return _currentNetwork;
        }
    }

    /// <inheritdoc />
    public async Task DisposeCurrentAsync()
    {
        NetworkExchangeContext? contextToDispose;

        lock (_lock)
        {
            if (_currentContext is null)
            {
                _logger.LogDebug("No current context to dispose");
                return;
            }

            contextToDispose = _currentContext;
            _currentContext = null;
            _currentNetwork = null;
        }

        _logger.LogInformation("Disposing exchange context for network {ExchangeId}", contextToDispose.ExchangeClient.ExchangeId);
        await contextToDispose.DisposeAsync();
        _logger.LogDebug("Exchange context disposed");
    }

    /// <inheritdoc />
    public IExtendedHttpClient? GetHttpClient()
    {
        lock (_lock)
        {
            return _currentContext?.ExtendedHttpClient;
        }
    }

    /// <inheritdoc />
    public NonceManager? GetNonceManager()
    {
        lock (_lock)
        {
            return _currentContext?.NonceManager;
        }
    }

    /// <inheritdoc />
    public StarkSigner? GetStarkSigner()
    {
        lock (_lock)
        {
            return _currentContext?.StarkSigner;
        }
    }

    /// <inheritdoc />
    public ExtendedOptions? GetCurrentOptions()
    {
        lock (_lock)
        {
            return _currentContext?.Options;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await DisposeCurrentAsync();

        _logger.LogDebug("ExtendedNetworkExchangeFactory disposed");
    }

    private async Task<NetworkExchangeContext> CreateNetworkContextAsync(
        ExtendedNetworkType network,
        ExtendedOptions options,
        CancellationToken ct)
    {
        var exchangeId = GetExchangeId(network);

        // Ensure IsTestnet is set correctly based on network
        options.IsTestnet = network == ExtendedNetworkType.Testnet;

        // Create options wrapper for this network
        var extendedOptionsWrapper = Options.Create(options);
        var wsOptionsWrapper = Options.Create(_networksOptions.WebSocket);

        // Create HTTP client for REST operations
        // Note: Headers are added by ExtendedHttpClient constructor, not here
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Create core infrastructure with proper dependencies
        var rateLimiterLogger = _loggerFactory.CreateLogger<RateLimiter>();
        var rateLimiter = new RateLimiter(extendedOptionsWrapper, rateLimiterLogger);

        var nonceManagerLogger = _loggerFactory.CreateLogger<NonceManager>();
        var nonceManager = new NonceManager(extendedOptionsWrapper, nonceManagerLogger);

        var marketMapperLogger = _loggerFactory.CreateLogger<ExtendedMarketMapper>();
        var marketMapper = new ExtendedMarketMapper(marketMapperLogger);

        var starkSignerLogger = _loggerFactory.CreateLogger<StarkSigner>();
        var starkSigner = new StarkSigner(extendedOptionsWrapper, starkSignerLogger);

        // Initialize StarkSigner
        starkSigner.Initialize();

        // Create HTTP client wrapper
        var httpClientLogger = _loggerFactory.CreateLogger<ExtendedHttpClient>();
        var extendedHttpClient = new ExtendedHttpClient(httpClient, rateLimiter, extendedOptionsWrapper, httpClientLogger);

        // Create WebSocket client
        var wsLogger = _loggerFactory.CreateLogger<ExtendedWebSocketClient>();
        var wsClient = new ExtendedWebSocketClient(extendedOptionsWrapper, wsOptionsWrapper, wsLogger);

        // Create adapters with proper dependencies
        var scalingLogger = _loggerFactory.CreateLogger<ExtendedScalingAdapter>();
        var scalingAdapter = new ExtendedScalingAdapter(marketMapper, scalingLogger);

        var marketDataLogger = _loggerFactory.CreateLogger<ExtendedMarketDataAdapter>();
        var marketDataAdapter = new ExtendedMarketDataAdapter(extendedHttpClient, marketMapper, marketDataLogger);

        var accountLogger = _loggerFactory.CreateLogger<ExtendedAccountAdapter>();
        var accountAdapter = new ExtendedAccountAdapter(extendedHttpClient, nonceManager, accountLogger);

        var authLogger = _loggerFactory.CreateLogger<ExtendedAuthAdapter>();
        var authAdapter = new ExtendedAuthAdapter(nonceManager, extendedHttpClient, extendedOptionsWrapper, authLogger);

        var orderLogger = _loggerFactory.CreateLogger<ExtendedOrderAdapter>();
        var orderAdapter = new ExtendedOrderAdapter(
            extendedHttpClient,
            nonceManager,
            scalingAdapter,
            starkSigner,
            extendedOptionsWrapper,
            orderLogger);

        // Start automatic orphan order cleanup timer
        orderAdapter.StartCleanupTimer();

        var realtimeLogger = _loggerFactory.CreateLogger<ExtendedRealtimeAdapter>();
        var realtimeAdapter = new ExtendedRealtimeAdapter(wsClient, extendedHttpClient, orderAdapter, realtimeLogger);

        var connectionLogger = _loggerFactory.CreateLogger<ExtendedConnectionAdapter>();
        var connectionAdapter = new ExtendedConnectionAdapter(
            exchangeId,
            wsClient,
            extendedHttpClient,
            marketMapper,
            nonceManager,
            realtimeAdapter,
            connectionLogger);

        // Create the exchange client
        var exchangeClient = new ExtendedExchangeClient(
            exchangeId,
            connectionAdapter,
            realtimeAdapter,
            orderAdapter,
            accountAdapter,
            marketDataAdapter,
            scalingAdapter,
            authAdapter,
            options.DryRun);

        // Connect to the exchange
        await connectionAdapter.ConnectAsync(ct);

        // Fetch account info to get L2Vault (collateralPosition) for orders
        try
        {
            var accountInfo = await extendedHttpClient.GetAccountInfoAsync(ct);
            if (!string.IsNullOrEmpty(accountInfo.L2Vault))
            {
                options.L2Vault = accountInfo.L2Vault;
                _logger.LogInformation("L2Vault retrieved: {L2Vault}", accountInfo.L2Vault);
            }
            else
            {
                _logger.LogWarning("Account has no L2Vault configured - order creation will fail");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch account info for L2Vault. Order creation may fail.");
        }

        return new NetworkExchangeContext(
            network,
            exchangeClient,
            httpClient,
            wsClient,
            starkSigner,
            rateLimiter,
            nonceManager,
            marketMapper,
            orderAdapter,
            extendedHttpClient,
            options);
    }

    private static string GetExchangeId(ExtendedNetworkType network) => network switch
    {
        ExtendedNetworkType.Testnet => "extended-testnet",
        ExtendedNetworkType.Mainnet => "extended-mainnet",
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "Unknown network type")
    };

    /// <summary>
    /// Holds all resources for a single network context.
    /// </summary>
    private sealed class NetworkExchangeContext : IAsyncDisposable
    {
        public ExtendedNetworkType Network { get; }
        public IExchangeClient ExchangeClient { get; }
        public HttpClient HttpClient { get; }
        public IExtendedWebSocketClient WebSocketClient { get; }
        public StarkSigner StarkSigner { get; }
        public RateLimiter RateLimiter { get; }
        public NonceManager NonceManager { get; }
        public ExtendedMarketMapper MarketMapper { get; }
        public ExtendedOrderAdapter OrderAdapter { get; }
        public IExtendedHttpClient ExtendedHttpClient { get; }
        public ExtendedOptions Options { get; }

        public NetworkExchangeContext(
            ExtendedNetworkType network,
            IExchangeClient exchangeClient,
            HttpClient httpClient,
            IExtendedWebSocketClient webSocketClient,
            StarkSigner starkSigner,
            RateLimiter rateLimiter,
            NonceManager nonceManager,
            ExtendedMarketMapper marketMapper,
            ExtendedOrderAdapter orderAdapter,
            IExtendedHttpClient extendedHttpClient,
            ExtendedOptions options)
        {
            Network = network;
            ExchangeClient = exchangeClient;
            HttpClient = httpClient;
            WebSocketClient = webSocketClient;
            StarkSigner = starkSigner;
            RateLimiter = rateLimiter;
            NonceManager = nonceManager;
            MarketMapper = marketMapper;
            OrderAdapter = orderAdapter;
            ExtendedHttpClient = extendedHttpClient;
            Options = options;
        }

        public async ValueTask DisposeAsync()
        {
            // Stop order cleanup timer
            OrderAdapter.StopCleanupTimer();

            // Dispose in reverse order of creation
            await ExchangeClient.DisposeAsync();
            StarkSigner.Dispose();
            HttpClient.Dispose();
            // WebSocketClient is disposed via ExchangeClient.Connection
        }
    }
}
