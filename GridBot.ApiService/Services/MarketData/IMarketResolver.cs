namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Resolves market symbols to their corresponding market IDs at runtime.
/// </summary>
public interface IMarketResolver
{
    /// <summary>
    /// Gets the resolved market ID for the configured symbol.
    /// Throws <see cref="InvalidOperationException"/> if not yet initialized.
    /// </summary>
    int MarketId { get; }

    /// <summary>
    /// Gets the configured symbol (e.g., "BTC").
    /// </summary>
    string Symbol { get; }

    /// <summary>
    /// Gets the full resolved symbol from the order book (e.g., "BTC-USDC").
    /// Throws <see cref="InvalidOperationException"/> if not yet initialized.
    /// </summary>
    string ResolvedSymbol { get; }

    /// <summary>
    /// Initializes the resolver by fetching order books and resolving the symbol.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if initialized successfully.
    /// </summary>
    bool IsInitialized { get; }
}
