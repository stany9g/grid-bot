using GridBot.ApiService.Services.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering state persistence services.
/// </summary>
public static class PersistenceServiceExtensions
{
    /// <summary>
    /// Adds state persistence services to the service collection.
    /// Registers IStateRepository with Redis-backed implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IStateRepository, RedisStateRepository>();

        return services;
    }
}
