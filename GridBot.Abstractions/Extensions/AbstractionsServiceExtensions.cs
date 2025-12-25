using GridBot.Abstractions.Factory;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.Abstractions.Extensions;

/// <summary>
/// Extension methods for registering abstraction services.
/// </summary>
public static class AbstractionsServiceExtensions
{
    /// <summary>
    /// Adds the exchange registry to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExchangeRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IExchangeRegistry, DefaultExchangeRegistry>();
        return services;
    }

    /// <summary>
    /// Adds an exchange factory to the service collection.
    /// </summary>
    /// <typeparam name="TFactory">The factory implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExchangeFactory<TFactory>(this IServiceCollection services)
        where TFactory : class, IExchangeFactory
    {
        services.AddSingleton<IExchangeFactory, TFactory>();
        return services;
    }
}

/// <summary>
/// Default implementation of the exchange registry.
/// </summary>
internal sealed class DefaultExchangeRegistry : IExchangeRegistry
{
    private readonly List<IExchangeClient> _clients = [];
    private readonly object _lock = new();

    /// <inheritdoc />
    public IReadOnlyList<IExchangeClient> GetAll()
    {
        lock (_lock)
        {
            return _clients.ToList();
        }
    }

    /// <inheritdoc />
    public IExchangeClient? Get(string exchangeId)
    {
        ArgumentNullException.ThrowIfNull(exchangeId);

        lock (_lock)
        {
            return _clients.Find(c => c.ExchangeId.Equals(exchangeId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IExchangeClient> GetByType(ExchangeType type)
    {
        lock (_lock)
        {
            return _clients.Where(c => c.ExchangeType == type).ToList();
        }
    }

    /// <inheritdoc />
    public IExchangeClient GetPrimary()
    {
        lock (_lock)
        {
            if (_clients.Count == 0)
            {
                throw new InvalidOperationException("No exchange clients are registered.");
            }

            return _clients[0];
        }
    }

    /// <inheritdoc />
    public void Register(IExchangeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            var existing = _clients.FindIndex(c => c.ExchangeId.Equals(client.ExchangeId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                throw new InvalidOperationException($"An exchange client with ID '{client.ExchangeId}' is already registered.");
            }

            _clients.Add(client);
        }
    }

    /// <inheritdoc />
    public bool Unregister(string exchangeId)
    {
        ArgumentNullException.ThrowIfNull(exchangeId);

        lock (_lock)
        {
            var index = _clients.FindIndex(c => c.ExchangeId.Equals(exchangeId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _clients.RemoveAt(index);
            return true;
        }
    }
}
