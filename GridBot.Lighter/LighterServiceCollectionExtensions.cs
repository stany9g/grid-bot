using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.Lighter;

/// <summary>
/// Extension methods for registering Lighter services with dependency injection.
/// </summary>
public static class LighterServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Lighter client to the service collection using configuration from appsettings.json.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing the "Lighter" section.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if configuration is invalid.</exception>
    public static IServiceCollection AddLighterClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new LighterOptions();
        configuration.GetSection(LighterOptions.SectionName).Bind(options);

        services.AddOptions<LighterOptions>()
            .Bind(configuration.GetSection(LighterOptions.SectionName));

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException($"Lighter configuration is invalid: {validationError}");

        return services.AddLighterClient(opts =>
        {
            opts.ApiUrl = options.ApiUrl;
            opts.PrivateKey = options.PrivateKey;
            opts.ChainId = options.ChainId;
            opts.ApiKeyIndex = options.ApiKeyIndex;
            opts.AccountIndex = options.AccountIndex;
            opts.InitialNonce = options.InitialNonce;
        });
    }

    /// <summary>
    /// Adds the Lighter client to the service collection using a configuration action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if configuration is invalid or SignerClient initialization fails.</exception>
    public static IServiceCollection AddLighterClient(
        this IServiceCollection services,
        Action<LighterOptions> configureOptions)
    {
        var options = new LighterOptions();
        configureOptions(options);

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException($"Lighter configuration is invalid: {validationError}");

        // Register query client with HttpClientFactory
        services.AddHttpClient<ILighterQueryClient, LighterQueryClient>((serviceProvider, client) =>
        {
            client.BaseAddress = new Uri($"{options.ApiUrl}/api/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Register write HttpClient separately
        services.AddHttpClient("LighterWriteClient", client =>
        {
            client.BaseAddress = new Uri($"{options.ApiUrl}/api/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Register command client as singleton
        services.AddSingleton<ILighterCommandClient>(serviceProvider =>
        {
            var queryClient = serviceProvider.GetRequiredService<ILighterQueryClient>();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var writeClient = httpClientFactory.CreateClient("LighterWriteClient");

            // Fetch the correct nonce from the server at startup
            long initialNonce;
            try
            {
                var nonceResponse = queryClient.GetNextNonceAsync(options.AccountIndex, options.ApiKeyIndex)
                    .GetAwaiter().GetResult();
                // Subtract 1 because GetNextNonce() does ++_currentNonce before returning
                initialNonce = nonceResponse.Nonce - 1;
                Console.WriteLine($"[LighterClient] Fetched nonce from server: {nonceResponse.Nonce}, setting initial nonce to {initialNonce}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LighterClient] Failed to fetch nonce from server, using config value {options.InitialNonce}: {ex.Message}");
                initialNonce = options.InitialNonce;
            }

            var signer = new SignerClient();
            var error = signer.InitializeAsync(
                options.ApiUrl,
                options.PrivateKey,
                options.ChainId,
                options.ApiKeyIndex,
                options.AccountIndex,
                initialNonce
            ).GetAwaiter().GetResult();

            if (error != null)
                throw new InvalidOperationException($"Failed to initialize SignerClient: {error}");

            return new LighterCommandClient(queryClient, writeClient, signer);
        });

        return services;
    }
}
