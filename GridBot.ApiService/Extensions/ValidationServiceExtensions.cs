using GridBot.ApiService.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering validation services.
/// </summary>
public static class ValidationServiceExtensions
{
    /// <summary>
    /// Adds pre-trade validation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddValidationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register pre-trade depth validator as singleton (stateless, uses WebSocket state)
        services.AddSingleton<IPreTradeValidator, PreTradeValidator>();

        return services;
    }
}
