using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter;

/// <summary>
/// Extension methods for registering Lighter services with dependency injection.
/// Uses WebSocket-first architecture with no REST fallback for queries.
/// </summary>
public static class LighterServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Lighter client to the service collection using configuration from appsettings.json.
    /// Configures WebSocket-based query and command clients.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing the "Lighter" and "LighterWebSocket" sections.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if configuration is invalid.</exception>
    public static IServiceCollection AddLighterClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind and validate Lighter options
        var options = new LighterOptions();
        configuration.GetSection(LighterOptions.SectionName).Bind(options);

        services.AddOptions<LighterOptions>()
            .Bind(configuration.GetSection(LighterOptions.SectionName));

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException($"Lighter configuration is invalid: {validationError}");

        // Bind WebSocket options
        services.AddOptions<WebSocketOptions>()
            .Bind(configuration.GetSection(WebSocketOptions.SectionName));

        // Validate native library early to catch platform/architecture issues
        var nativeError = SignerClient.ValidateNativeLibrary();
        if (nativeError != null)
        {
            throw new InvalidOperationException($"Native library validation failed: {nativeError}");
        }

        // Register SignerClient as singleton (shared by WebSocket and command clients)
        services.AddSingleton(serviceProvider =>
        {
            var signer = new SignerClient();
            var error = signer.InitializeAsync(
                options.ApiUrl,
                options.PrivateKey,
                options.ChainId,
                options.ApiKeyIndex,
                options.AccountIndex,
                options.InitialNonce
            ).GetAwaiter().GetResult();

            if (error != null)
                throw new InvalidOperationException($"Failed to initialize SignerClient: {error}");

            return signer;
        });

        // Register WebSocket client as singleton
        services.AddSingleton<ILighterWebSocketClient>(serviceProvider =>
        {
            var signerClient = serviceProvider.GetRequiredService<SignerClient>();
            var wsOptions = serviceProvider.GetRequiredService<IOptions<WebSocketOptions>>();
            var lighterOptions = serviceProvider.GetRequiredService<IOptions<LighterOptions>>();
            var logger = serviceProvider.GetRequiredService<ILogger<LighterWebSocketClient>>();

            return new LighterWebSocketClient(signerClient, wsOptions, lighterOptions, logger);
        });

        // Register real-time state service as singleton (implements ILighterRealtimeState)
        services.AddSingleton<LighterRealtimeStateService>();
        services.AddSingleton<ILighterRealtimeState>(sp => sp.GetRequiredService<LighterRealtimeStateService>());

        // Register as hosted service to start WebSocket processing
        services.AddHostedService(sp => sp.GetRequiredService<LighterRealtimeStateService>());

        // Register WebSocket-based query client (uses REST for market list and candlesticks)
        services.AddSingleton<ILighterQueryClient>(serviceProvider =>
        {
            var state = serviceProvider.GetRequiredService<ILighterRealtimeState>();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LighterCommandClient");
            var logger = serviceProvider.GetRequiredService<ILogger<WsLighterQueryClient>>();

            return new WsLighterQueryClient(state, httpClient, logger);
        });

        // Register HTTP client for command submission (commands require HTTP POST)
        services.AddHttpClient("LighterCommandClient", client =>
        {
            client.BaseAddress = new Uri($"{options.ApiUrl}/api/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Register WebSocket-based command client
        services.AddSingleton<ILighterCommandClient>(serviceProvider =>
        {
            var signer = serviceProvider.GetRequiredService<SignerClient>();
            var state = serviceProvider.GetRequiredService<ILighterRealtimeState>();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LighterCommandClient");
            var logger = serviceProvider.GetRequiredService<ILogger<WsLighterCommandClient>>();

            return new WsLighterCommandClient(signer, state, httpClient, logger);
        });

        return services;
    }
}
