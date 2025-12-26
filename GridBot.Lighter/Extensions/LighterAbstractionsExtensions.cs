using System.Collections.Concurrent;
using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Extensions;
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using GridBot.Lighter.Adapters;
using GridBot.Lighter.Factory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter.Extensions;

/// <summary>
/// Extension methods for registering Lighter exchange adapters with the abstraction layer.
/// </summary>
public static class LighterAbstractionsExtensions
{
    /// <summary>
    /// Adds the Lighter exchange client implementing the abstraction interfaces.
    /// Registers all adapters and the aggregate <see cref="IExchangeClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing the "Lighter" and "LighterWebSocket" sections.</param>
    /// <param name="exchangeId">Unique identifier for this exchange instance. Defaults to "lighter-main".</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method calls <see cref="LighterServiceCollectionExtensions.AddLighterClient"/> internally
    /// to register the core Lighter services, then adds the abstraction adapters on top.
    ///
    /// The exchange client is registered as a keyed service using the exchangeId, allowing
    /// multiple exchange instances to coexist in the same DI container.
    /// </remarks>
    public static IServiceCollection AddLighterExchange(
        this IServiceCollection services,
        IConfiguration configuration,
        string exchangeId = "lighter-main")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeId);

        // Register core Lighter services (SignerClient, WebSocket, CommandClient, QueryClient, etc.)
        services.AddLighterClient(configuration);

        // Register the market mapper as singleton
        services.AddSingleton<LighterMarketMapper>();

        // Register the scaling adapter as singleton implementing IScalingProvider
        services.AddSingleton<LighterScalingAdapter>();
        services.AddSingleton<IScalingProvider>(sp => sp.GetRequiredService<LighterScalingAdapter>());

        // Register individual adapters as singletons
        services.AddSingleton<LighterConnectionAdapter>(sp =>
        {
            var wsClient = sp.GetRequiredService<ILighterWebSocketClient>();
            var realtimeState = sp.GetRequiredService<ILighterRealtimeState>();
            var logger = sp.GetRequiredService<ILogger<LighterConnectionAdapter>>();

            return new LighterConnectionAdapter(wsClient, realtimeState, exchangeId, logger);
        });
        services.AddSingleton<IExchangeConnection>(sp => sp.GetRequiredService<LighterConnectionAdapter>());

        services.AddSingleton<LighterRealtimeAdapter>(sp =>
        {
            var realtimeState = sp.GetRequiredService<ILighterRealtimeState>();
            var wsClient = sp.GetRequiredService<ILighterWebSocketClient>();
            var marketMapper = sp.GetRequiredService<LighterMarketMapper>();
            var logger = sp.GetRequiredService<ILogger<LighterRealtimeAdapter>>();

            return new LighterRealtimeAdapter(realtimeState, wsClient, marketMapper, logger);
        });
        services.AddSingleton<IRealtimeDataProvider>(sp => sp.GetRequiredService<LighterRealtimeAdapter>());

        services.AddSingleton<LighterOrderAdapter>(sp =>
        {
            var commandClient = sp.GetRequiredService<ILighterCommandClient>();
            var scalingProvider = sp.GetRequiredService<IScalingProvider>();
            var marketMapper = sp.GetRequiredService<LighterMarketMapper>();
            var logger = sp.GetRequiredService<ILogger<LighterOrderAdapter>>();

            return new LighterOrderAdapter(commandClient, scalingProvider, marketMapper, logger);
        });
        services.AddSingleton<IOrderClient>(sp => sp.GetRequiredService<LighterOrderAdapter>());

        services.AddSingleton<LighterAccountAdapter>(sp =>
        {
            var queryClient = sp.GetRequiredService<ILighterQueryClient>();
            var commandClient = sp.GetRequiredService<ILighterCommandClient>();
            var realtimeState = sp.GetRequiredService<ILighterRealtimeState>();
            var marketMapper = sp.GetRequiredService<LighterMarketMapper>();
            var options = sp.GetRequiredService<IOptions<LighterOptions>>();
            var logger = sp.GetRequiredService<ILogger<LighterAccountAdapter>>();

            return new LighterAccountAdapter(queryClient, commandClient, realtimeState, marketMapper, options, logger);
        });
        services.AddSingleton<IAccountClient>(sp => sp.GetRequiredService<LighterAccountAdapter>());

        services.AddSingleton<LighterMarketDataAdapter>(sp =>
        {
            var queryClient = sp.GetRequiredService<ILighterQueryClient>();
            var realtimeState = sp.GetRequiredService<ILighterRealtimeState>();
            var marketMapper = sp.GetRequiredService<LighterMarketMapper>();
            var logger = sp.GetRequiredService<ILogger<LighterMarketDataAdapter>>();

            return new LighterMarketDataAdapter(queryClient, realtimeState, marketMapper, logger);
        });
        services.AddSingleton<IMarketDataClient>(sp => sp.GetRequiredService<LighterMarketDataAdapter>());

        services.AddSingleton<LighterAuthAdapter>(sp =>
        {
            var signerClient = sp.GetRequiredService<SignerClient>();
            var commandClient = sp.GetRequiredService<ILighterCommandClient>();
            var options = sp.GetRequiredService<IOptions<LighterOptions>>();
            var logger = sp.GetRequiredService<ILogger<LighterAuthAdapter>>();

            return new LighterAuthAdapter(signerClient, commandClient, options, logger);
        });
        services.AddSingleton<IAuthenticationProvider>(sp => sp.GetRequiredService<LighterAuthAdapter>());

        // Register the aggregate exchange client as keyed singleton
        services.AddKeyedSingleton<IExchangeClient>(exchangeId, (sp, key) =>
        {
            var connection = sp.GetRequiredService<IExchangeConnection>();
            var realtimeData = sp.GetRequiredService<IRealtimeDataProvider>();
            var orders = sp.GetRequiredService<IOrderClient>();
            var account = sp.GetRequiredService<IAccountClient>();
            var marketData = sp.GetRequiredService<IMarketDataClient>();
            var scaling = sp.GetRequiredService<IScalingProvider>();
            var auth = sp.GetRequiredService<IAuthenticationProvider>();
            var options = sp.GetRequiredService<IOptions<LighterOptions>>();
            var logger = sp.GetRequiredService<ILogger<LighterExchangeClient>>();

            return new LighterExchangeClient(
                (string)key!,
                connection,
                realtimeData,
                orders,
                account,
                marketData,
                scaling,
                auth,
                options,
                logger);
        });

        // Also register as default (non-keyed) for single-exchange scenarios
        services.AddSingleton<IExchangeClient>(sp =>
            sp.GetRequiredKeyedService<IExchangeClient>(exchangeId));

        return services;
    }

    /// <summary>
    /// Initializes the Lighter exchange by loading market metadata and connecting.
    /// Call this after the service provider is built, typically in a hosted service.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The initialized exchange client.</returns>
    public static async Task<IExchangeClient> InitializeLighterExchangeAsync(
        this IServiceProvider serviceProvider,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var logger = serviceProvider.GetRequiredService<ILogger<LighterMarketMapper>>();
        var marketMapper = serviceProvider.GetRequiredService<LighterMarketMapper>();
        var queryClient = serviceProvider.GetRequiredService<ILighterQueryClient>();
        var exchangeClient = serviceProvider.GetRequiredService<IExchangeClient>();

        // Initialize market mapper with market data
        await marketMapper.InitializeAsync(queryClient, ct);

        // Connect to the exchange
        await exchangeClient.Connection.ConnectAsync(ct);

        logger.LogInformation(
            "Lighter exchange initialized: {ExchangeId}, Markets: {MarketCount}",
            exchangeClient.ExchangeId,
            marketMapper.GetAllMarketIds().Count);

        return exchangeClient;
    }

    /// <summary>
    /// Service key for testnet exchange client.
    /// </summary>
    public const string TestnetServiceKey = "lighter-testnet";

    /// <summary>
    /// Service key for mainnet exchange client.
    /// </summary>
    public const string MainnetServiceKey = "lighter-mainnet";

    /// <summary>
    /// Adds Lighter network configuration and factory services to the service collection.
    /// This registers <see cref="LighterNetworksOptions"/> and <see cref="INetworkExchangeFactory"/>
    /// for runtime network switching between testnet and mainnet.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing the "LighterNetworks" and "LighterWebSocket" sections.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers:
    /// - LighterNetworksOptions from configuration
    /// - WebSocketOptions from configuration
    /// - INetworkExchangeFactory for creating exchange clients per network
    /// - IExchangeRegistry for managing registered exchange clients
    /// - HTTP client factory for REST operations
    ///
    /// Networks are initialized on-demand when selected via the NetworkSelectionService.
    /// The factory creates exchange clients lazily and caches them.
    /// </remarks>
    public static IServiceCollection AddLighterNetworks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind LighterNetworksOptions from configuration
        services.AddOptions<LighterNetworksOptions>()
            .Bind(configuration.GetSection(LighterNetworksOptions.SectionName));

        // Bind WebSocket options
        services.AddOptions<WebSocketOptions>()
            .Bind(configuration.GetSection(WebSocketOptions.SectionName));

        // Validate native library early to catch platform/architecture issues
        var nativeError = SignerClient.ValidateNativeLibrary();
        if (nativeError != null)
        {
            throw new InvalidOperationException($"Native library validation failed: {nativeError}");
        }

        // Register HTTP client for REST operations
        services.AddHttpClient();

        // Register the exchange registry
        services.AddExchangeRegistry();

        // Register the network exchange factory
        services.AddSingleton<INetworkExchangeFactory, NetworkExchangeFactory>();

        // Register forwarding services that delegate to the current exchange client
        // These services resolve the primary exchange from the registry at call time
        services.AddScoped<IAccountClient>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().Account;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IMarketDataClient>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().MarketData;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IOrderClient>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().Orders;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IScalingProvider>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().Scaling;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IAuthenticationProvider>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().Auth;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IExchangeConnection>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().Connection;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        services.AddScoped<IRealtimeDataProvider>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary().RealtimeData;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        // Register forwarding for IExchangeClient (non-keyed)
        services.AddScoped<IExchangeClient>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            try
            {
                return registry.GetPrimary();
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "No exchange client is registered. Select a network first.");
            }
        });

        return services;
    }

    /// <summary>
    /// Gets the service key for a network type.
    /// </summary>
    /// <param name="network">The network type.</param>
    /// <returns>The service key for the network.</returns>
    public static string GetServiceKey(LighterNetworkType network) => network switch
    {
        LighterNetworkType.Testnet => TestnetServiceKey,
        LighterNetworkType.Mainnet => MainnetServiceKey,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "Unknown network type")
    };
}
