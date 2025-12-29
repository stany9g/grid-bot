namespace GridBot.Abstractions.Factory;

/// <summary>
/// Registry for managing multiple exchange client instances.
/// Supports multi-DEX trading scenarios.
/// </summary>
public interface IExchangeRegistry
{
    /// <summary>
    /// Gets all registered exchange clients.
    /// </summary>
    /// <returns>List of all exchange clients.</returns>
    IReadOnlyList<IExchangeClient> GetAll();

    /// <summary>
    /// Gets an exchange client by its identifier.
    /// </summary>
    /// <param name="exchangeId">The exchange identifier.</param>
    /// <returns>The exchange client, or null if not found.</returns>
    IExchangeClient? Get(string exchangeId);

    /// <summary>
    /// Gets all exchange clients of a specific type.
    /// </summary>
    /// <param name="type">The exchange type.</param>
    /// <returns>List of matching exchange clients.</returns>
    IReadOnlyList<IExchangeClient> GetByType(ExchangeType type);

    /// <summary>
    /// Gets the primary (default) exchange client.
    /// </summary>
    /// <returns>The primary exchange client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no exchange is registered.</exception>
    IExchangeClient GetPrimary();

    /// <summary>
    /// Registers an exchange client.
    /// </summary>
    /// <param name="client">The exchange client to register.</param>
    void Register(IExchangeClient client);

    /// <summary>
    /// Unregisters an exchange client.
    /// </summary>
    /// <param name="exchangeId">The exchange identifier to unregister.</param>
    /// <returns>True if the client was found and removed, false otherwise.</returns>
    bool Unregister(string exchangeId);

    /// <summary>
    /// Sets the primary (default) exchange client.
    /// </summary>
    /// <param name="exchangeId">The exchange identifier to set as primary.</param>
    /// <exception cref="ArgumentException">Thrown when the exchange is not registered.</exception>
    void SetPrimary(string exchangeId);
}
