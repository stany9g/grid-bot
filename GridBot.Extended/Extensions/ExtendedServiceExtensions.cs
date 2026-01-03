using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Extensions;
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using GridBot.Extended.Adapters;
using GridBot.Extended.Factory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended.Extensions;

/// <summary>
/// Extension methods for registering Extended exchange services.
/// </summary>
public static class ExtendedServiceExtensions
{
    /// <summary>
    /// Adds Extended exchange services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="exchangeId">The exchange ID (default: "extended-main").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExtendedExchange(
        this IServiceCollection services,
        IConfiguration configuration,
        string exchangeId = "extended-main")
    {
        // 1. Bind and validate configuration
        var options = new ExtendedOptions();
        configuration.GetSection(ExtendedOptions.SectionName).Bind(options);

        var validationError = options.Validate();
        if (validationError != null)
        {
            throw new InvalidOperationException($"Extended configuration error: {validationError}");
        }

        services.Configure<ExtendedOptions>(configuration.GetSection(ExtendedOptions.SectionName));

        // 2. Register HTTP client
        services.AddHttpClient("ExtendedClient", client =>
        {
            client.BaseAddress = new Uri(options.ApiUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, options.ApiKey);
            client.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
        });

        // 3. Register core infrastructure
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<NonceManager>();
        services.AddSingleton<ExtendedMarketMapper>();
        services.AddSingleton<StarkSigner>();

        // 4. Register HTTP client implementation
        services.AddSingleton<IExtendedHttpClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("ExtendedClient");
            var rateLimiter = sp.GetRequiredService<RateLimiter>();
            var opts = sp.GetRequiredService<IOptions<ExtendedOptions>>();
            var logger = sp.GetRequiredService<ILogger<ExtendedHttpClient>>();

            return new ExtendedHttpClient(httpClient, rateLimiter, opts, logger);
        });

        // Configure WebSocket options (bind from configuration or use defaults)
        services.Configure<WebSocketOptions>(configuration.GetSection("Extended:WebSocket"));

        // 5. Register WebSocket client
        services.AddSingleton<IExtendedWebSocketClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ExtendedOptions>>();
            var wsOpts = sp.GetRequiredService<IOptions<WebSocketOptions>>();
            var logger = sp.GetRequiredService<ILogger<ExtendedWebSocketClient>>();

            return new ExtendedWebSocketClient(opts, wsOpts, logger);
        });

        // 6. Register adapters
        services.AddSingleton<ExtendedScalingAdapter>();
        services.AddSingleton<ExtendedMarketDataAdapter>();
        services.AddSingleton<ExtendedAccountAdapter>();
        services.AddSingleton<ExtendedAuthAdapter>();

        // Order adapter requires StarkSigner for order signing
        services.AddSingleton<ExtendedOrderAdapter>(sp =>
        {
            var httpClient = sp.GetRequiredService<IExtendedHttpClient>();
            var nonceManager = sp.GetRequiredService<NonceManager>();
            var scalingAdapter = sp.GetRequiredService<ExtendedScalingAdapter>();
            var starkSigner = sp.GetRequiredService<StarkSigner>();
            var opts = sp.GetRequiredService<IOptions<ExtendedOptions>>();
            var logger = sp.GetRequiredService<ILogger<ExtendedOrderAdapter>>();

            // Initialize StarkSigner on first use
            starkSigner.Initialize();

            var adapter = new ExtendedOrderAdapter(
                httpClient,
                nonceManager,
                scalingAdapter,
                starkSigner,
                opts,
                logger);

            // Start automatic orphan order cleanup timer
            adapter.StartCleanupTimer();

            return adapter;
        });

        // 7. Register realtime adapter (needs special handling for channel consumption)
        services.AddSingleton<ExtendedRealtimeAdapter>(sp =>
        {
            var wsClient = sp.GetRequiredService<IExtendedWebSocketClient>();
            var httpClient = sp.GetRequiredService<IExtendedHttpClient>();
            var orderAdapter = sp.GetRequiredService<ExtendedOrderAdapter>();
            var logger = sp.GetRequiredService<ILogger<ExtendedRealtimeAdapter>>();

            return new ExtendedRealtimeAdapter(wsClient, httpClient, orderAdapter, logger);
        });

        // 8. Register connection adapter
        services.AddSingleton<ExtendedConnectionAdapter>(sp =>
        {
            var wsClient = sp.GetRequiredService<IExtendedWebSocketClient>();
            var httpClient = sp.GetRequiredService<IExtendedHttpClient>();
            var marketMapper = sp.GetRequiredService<ExtendedMarketMapper>();
            var nonceManager = sp.GetRequiredService<NonceManager>();
            var realtimeAdapter = sp.GetRequiredService<ExtendedRealtimeAdapter>();
            var logger = sp.GetRequiredService<ILogger<ExtendedConnectionAdapter>>();

            return new ExtendedConnectionAdapter(
                exchangeId,
                wsClient,
                httpClient,
                marketMapper,
                nonceManager,
                realtimeAdapter,
                logger);
        });

        // 9. Register keyed IExchangeClient
        services.AddKeyedSingleton<IExchangeClient>(exchangeId, (sp, key) =>
        {
            var connectionAdapter = sp.GetRequiredService<ExtendedConnectionAdapter>();
            var realtimeAdapter = sp.GetRequiredService<ExtendedRealtimeAdapter>();
            var orderAdapter = sp.GetRequiredService<ExtendedOrderAdapter>();
            var accountAdapter = sp.GetRequiredService<ExtendedAccountAdapter>();
            var marketDataAdapter = sp.GetRequiredService<ExtendedMarketDataAdapter>();
            var scalingAdapter = sp.GetRequiredService<ExtendedScalingAdapter>();
            var authAdapter = sp.GetRequiredService<ExtendedAuthAdapter>();
            var opts = sp.GetRequiredService<IOptions<ExtendedOptions>>();

            return new ExtendedExchangeClient(
                exchangeId,
                connectionAdapter,
                realtimeAdapter,
                orderAdapter,
                accountAdapter,
                marketDataAdapter,
                scalingAdapter,
                authAdapter,
                opts.Value.DryRun);
        });

        // 10. Register with IExchangeRegistry if available
        services.AddHostedService<ExtendedExchangeRegistrationService>(sp =>
        {
            return new ExtendedExchangeRegistrationService(sp, exchangeId);
        });

        return services;
    }
}

/// <summary>
/// Background service that registers the Extended exchange client with the registry.
/// </summary>
internal sealed class ExtendedExchangeRegistrationService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _exchangeId;

    public ExtendedExchangeRegistrationService(IServiceProvider serviceProvider, string exchangeId)
    {
        _serviceProvider = serviceProvider;
        _exchangeId = exchangeId;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Try to register with the exchange registry if it exists
        var registry = _serviceProvider.GetService<IExchangeRegistry>();
        if (registry != null)
        {
            var client = _serviceProvider.GetRequiredKeyedService<IExchangeClient>(_exchangeId);
            registry.Register(client);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Extended network service extensions for multi-network support.
/// </summary>
public static class ExtendedNetworkServiceExtensions
{
    /// <summary>
    /// Service key for testnet exchange client.
    /// </summary>
    public const string TestnetServiceKey = "extended-testnet";

    /// <summary>
    /// Service key for mainnet exchange client.
    /// </summary>
    public const string MainnetServiceKey = "extended-mainnet";

    /// <summary>
    /// Adds Extended network configuration and factory services to the service collection.
    /// This registers <see cref="ExtendedNetworksOptions"/> and <see cref="IExtendedNetworkExchangeFactory"/>
    /// for runtime network switching between testnet and mainnet.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing the "ExtendedNetworks" section.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExtendedNetworks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind ExtendedNetworksOptions from configuration
        services.AddOptions<ExtendedNetworksOptions>()
            .Bind(configuration.GetSection(ExtendedNetworksOptions.SectionName));

        // Register HTTP client factory
        services.AddHttpClient();

        // Register the exchange registry if not already registered
        services.AddExchangeRegistry();

        // Register the network exchange factory
        services.AddSingleton<IExtendedNetworkExchangeFactory, ExtendedNetworkExchangeFactory>();

        // Register forwarding services that delegate to the current exchange client
        // These services resolve the primary exchange from the registry at call time
        // Note: These are already registered by AddLighterNetworks, so we skip if already registered
        // The registry-based forwarding in LighterAbstractionsExtensions handles both exchanges

        return services;
    }

    /// <summary>
    /// Gets the service key for a network type.
    /// </summary>
    /// <param name="network">The network type.</param>
    /// <returns>The service key for the network.</returns>
    public static string GetServiceKey(ExtendedNetworkType network) => network switch
    {
        ExtendedNetworkType.Testnet => TestnetServiceKey,
        ExtendedNetworkType.Mainnet => MainnetServiceKey,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "Unknown network type")
    };
}
