namespace GridBot.Abstractions.Communication;

/// <summary>
/// Manages the connection lifecycle to an exchange.
/// </summary>
public interface IExchangeConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this exchange connection.
    /// </summary>
    string ExchangeId { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Gets whether the connection is currently healthy and operational.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Gets the age of the most recent data received, or null if no data has been received.
    /// </summary>
    TimeSpan? DataAge { get; }

    /// <summary>
    /// Gets the number of disconnections in the last 24 hours.
    /// </summary>
    int DisconnectCount24h { get; }

    /// <summary>
    /// Occurs when the connection health changes.
    /// </summary>
    event EventHandler<ConnectionHealthEventArgs>? HealthChanged;

    /// <summary>
    /// Establishes a connection to the exchange.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the connection operation.</returns>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnects from the exchange gracefully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the disconnection operation.</returns>
    Task DisconnectAsync(CancellationToken ct = default);
}
