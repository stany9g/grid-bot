using System.Collections.Concurrent;
using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using GridBot.Lighter.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter.Factory;

/// <summary>
/// Factory that creates and manages Lighter exchange clients for different networks.
/// Clients are created lazily and cached. Only one network can be active at a time.
/// </summary>
/// <remarks>
/// Thread-safe implementation using locks for state management.
/// </remarks>
public sealed class NetworkExchangeFactory : INetworkExchangeFactory, IAsyncDisposable
{
    private readonly LighterNetworksOptions _networksOptions;
    private readonly IOptions<WebSocketOptions> _webSocketOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<NetworkExchangeFactory> _logger;
    private readonly object _lock = new();

    private LighterNetworkType? _currentNetwork;
    private NetworkExchangeContext? _currentContext;
    private bool _disposed;

    public NetworkExchangeFactory(
        IOptions<LighterNetworksOptions> networksOptions,
        IOptions<WebSocketOptions> webSocketOptions,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(networksOptions);
        ArgumentNullException.ThrowIfNull(webSocketOptions);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _networksOptions = networksOptions.Value;
        _webSocketOptions = webSocketOptions;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<NetworkExchangeFactory>();
    }

    /// <inheritdoc />
    public async Task<IExchangeClient> CreateForNetworkAsync(LighterNetworkType network, CancellationToken ct = default)
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
    public bool IsInitialized(LighterNetworkType network)
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
    public LighterNetworkType? GetCurrentNetwork()
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
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await DisposeCurrentAsync();

        _logger.LogDebug("NetworkExchangeFactory disposed");
    }

    private async Task<NetworkExchangeContext> CreateNetworkContextAsync(
        LighterNetworkType network,
        LighterOptions options,
        CancellationToken ct)
    {
        var exchangeId = GetExchangeId(network);

        // Create SignerClient for this network
        var signerClient = new SignerClient();
        var signerError = await signerClient.InitializeAsync(
            options.ApiUrl,
            options.PrivateKey,
            options.ChainId,
            options.ApiKeyIndex,
            options.AccountIndex,
            options.InitialNonce);

        if (signerError is not null)
        {
            signerClient.Dispose();
            throw new InvalidOperationException($"Failed to initialize SignerClient for {network}: {signerError}");
        }

        // Create options wrapper for this network
        var lighterOptionsWrapper = Options.Create(options);

        // Create WebSocket client
        var wsLogger = _loggerFactory.CreateLogger<LighterWebSocketClient>();
        var wsClient = new LighterWebSocketClient(signerClient, _webSocketOptions, lighterOptionsWrapper, wsLogger);

        // Create HTTP client for REST operations
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri($"{options.ApiUrl}/api/v1/");
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Create realtime state service
        var realtimeStateLogger = _loggerFactory.CreateLogger<LighterRealtimeStateService>();
        var realtimeStateService = new LighterRealtimeStateService(wsClient, signerClient, httpClient, lighterOptionsWrapper, realtimeStateLogger);

        // Create query client
        var queryLogger = _loggerFactory.CreateLogger<WsLighterQueryClient>();
        var queryClient = new WsLighterQueryClient(realtimeStateService, httpClient, queryLogger);

        // Create command client
        var commandLogger = _loggerFactory.CreateLogger<WsLighterCommandClient>();
        ILighterCommandClient commandClient = new WsLighterCommandClient(signerClient, realtimeStateService, wsClient, commandLogger, httpClient);

        // Wrap with DryRunCommandClient if needed
        if (options.DryRun)
        {
            var dryRunLogger = _loggerFactory.CreateLogger<DryRunCommandClient>();
            commandClient = new DryRunCommandClient(commandClient, dryRunLogger);
        }

        // Create market mapper and initialize it
        var marketMapperLogger = _loggerFactory.CreateLogger<LighterMarketMapper>();
        var marketMapper = new LighterMarketMapper(marketMapperLogger);
        await marketMapper.InitializeAsync(queryClient, ct);

        // Create adapters
        var scalingLogger = _loggerFactory.CreateLogger<LighterScalingAdapter>();
        var scalingAdapter = new LighterScalingAdapter(marketMapper, scalingLogger);

        var connectionLogger = _loggerFactory.CreateLogger<LighterConnectionAdapter>();
        var connectionAdapter = new LighterConnectionAdapter(wsClient, realtimeStateService, exchangeId, connectionLogger);

        var realtimeLogger = _loggerFactory.CreateLogger<LighterRealtimeAdapter>();
        var realtimeAdapter = new LighterRealtimeAdapter(realtimeStateService, wsClient, marketMapper, realtimeLogger);

        var orderLogger = _loggerFactory.CreateLogger<LighterOrderAdapter>();
        var orderAdapter = new LighterOrderAdapter(commandClient, scalingAdapter, marketMapper, orderLogger);

        var accountLogger = _loggerFactory.CreateLogger<LighterAccountAdapter>();
        var accountAdapter = new LighterAccountAdapter(queryClient, commandClient, realtimeStateService, marketMapper, lighterOptionsWrapper, accountLogger);

        var marketDataLogger = _loggerFactory.CreateLogger<LighterMarketDataAdapter>();
        var marketDataAdapter = new LighterMarketDataAdapter(queryClient, realtimeStateService, marketMapper, marketDataLogger);

        var authLogger = _loggerFactory.CreateLogger<LighterAuthAdapter>();
        var authAdapter = new LighterAuthAdapter(signerClient, commandClient, lighterOptionsWrapper, authLogger);

        // Create the exchange client
        var exchangeLogger = _loggerFactory.CreateLogger<LighterExchangeClient>();
        var exchangeClient = new LighterExchangeClient(
            exchangeId,
            connectionAdapter,
            realtimeAdapter,
            orderAdapter,
            accountAdapter,
            marketDataAdapter,
            scalingAdapter,
            authAdapter,
            lighterOptionsWrapper,
            exchangeLogger);

        // Connect to the exchange
        await connectionAdapter.ConnectAsync(ct);

        return new NetworkExchangeContext(
            network,
            exchangeClient,
            signerClient,
            wsClient,
            realtimeStateService,
            httpClient);
    }

    private static string GetExchangeId(LighterNetworkType network) => network switch
    {
        LighterNetworkType.Testnet => "lighter-testnet",
        LighterNetworkType.Mainnet => "lighter-mainnet",
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "Unknown network type")
    };

    /// <summary>
    /// Holds all resources for a single network context.
    /// </summary>
    private sealed class NetworkExchangeContext : IAsyncDisposable
    {
        public LighterNetworkType Network { get; }
        public IExchangeClient ExchangeClient { get; }
        public SignerClient SignerClient { get; }
        public ILighterWebSocketClient WebSocketClient { get; }
        public LighterRealtimeStateService RealtimeStateService { get; }
        public HttpClient HttpClient { get; }

        public NetworkExchangeContext(
            LighterNetworkType network,
            IExchangeClient exchangeClient,
            SignerClient signerClient,
            ILighterWebSocketClient webSocketClient,
            LighterRealtimeStateService realtimeStateService,
            HttpClient httpClient)
        {
            Network = network;
            ExchangeClient = exchangeClient;
            SignerClient = signerClient;
            WebSocketClient = webSocketClient;
            RealtimeStateService = realtimeStateService;
            HttpClient = httpClient;
        }

        public async ValueTask DisposeAsync()
        {
            // Dispose in reverse order of creation
            await ExchangeClient.DisposeAsync();
            SignerClient.Dispose();
            HttpClient.Dispose();
            // WebSocketClient and RealtimeStateService are disposed via ExchangeClient.Connection
        }
    }
}
