namespace GridBot.Abstractions.Factory;

/// <summary>
/// Factory for creating exchange client instances.
/// </summary>
public interface IExchangeFactory
{
    /// <summary>
    /// Gets the exchange type this factory creates.
    /// </summary>
    ExchangeType ExchangeType { get; }

    /// <summary>
    /// Creates a new exchange client instance.
    /// </summary>
    /// <param name="exchangeId">Unique identifier for this exchange instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured exchange client.</returns>
    Task<IExchangeClient> CreateAsync(string exchangeId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new exchange client instance with dry-run mode enabled.
    /// </summary>
    /// <param name="exchangeId">Unique identifier for this exchange instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured exchange client in dry-run mode.</returns>
    Task<IExchangeClient> CreateDryRunAsync(string exchangeId, CancellationToken ct = default);
}
