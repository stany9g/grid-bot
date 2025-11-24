using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Buffers.Text;

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
        // Bind configuration
        var section = configuration.GetSection(LighterOptions.SectionName);
        services.Configure<LighterOptions>(section);

        // Validate configuration
        var options = new LighterOptions();
        section.Bind(options);

        if (options == null)
            throw new InvalidOperationException(
                $"Missing '{LighterOptions.SectionName}' configuration section in appsettings.json");

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException(
                $"Invalid Lighter configuration: {validationError}");

        // Register ILighterClient as a singleton
        services.AddSingleton<ILighterClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LighterOptions>>().Value;
            var client = new LighterClient(opts.ApiUrl);

            // Initialize synchronously (consider using async factory in production)
            var initTask = client.Signer.InitializeAsync(
                url: opts.ApiUrl,
                privateKey: opts.PrivateKey,
                chainId: opts.ChainId,
                apiKeyIndex: opts.ApiKeyIndex,
                accountIndex: opts.AccountIndex,
                initialNonce: opts.InitialNonce
            );

            initTask.Wait();

            if (initTask.Result != null)
                throw new InvalidOperationException(
                    $"Failed to initialize Lighter client: {initTask.Result}");

            return client;
        });

        return services;
    }

    /// <summary>
    /// Adds the Lighter client to the service collection using a configuration action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLighterClient(
        this IServiceCollection services,
        Action<LighterOptions> configureOptions)
    {
        var options = new LighterOptions();
        configureOptions(options);

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException(
                $"Invalid Lighter configuration: {validationError}");

        services.AddSingleton<ILighterClient>(sp =>
        {
            var client = new LighterClient(options.ApiUrl);

            var initTask = client.Signer.InitializeAsync(
                url: options.ApiUrl,
                privateKey: options.PrivateKey,
                chainId: options.ChainId,
                apiKeyIndex: options.ApiKeyIndex,
                accountIndex: options.AccountIndex,
                initialNonce: options.InitialNonce
            );

            initTask.Wait();

            if (initTask.Result != null)
                throw new InvalidOperationException(
                    $"Failed to initialize Lighter client: {initTask.Result}");

            return client;
        });

        return services;
    }
}
