namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Available market information.
/// </summary>
public sealed record MarketInfo(int MarketId, string Symbol, string BaseAsset, bool IsActive);

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
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if initialized successfully.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Resolves a market symbol to its market ID.
    /// </summary>
    /// <param name="symbol">Symbol like "BTC" or "ETH".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Market ID, or null if not found.</returns>
    Task<int?> ResolveMarketIndexAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available markets from the exchange.
    /// </summary>
    Task<IReadOnlyList<MarketInfo>> GetAvailableMarketsAsync(CancellationToken cancellationToken = default);
}
