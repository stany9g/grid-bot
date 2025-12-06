using GridBot.ApiService.Configuration;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Resolves market symbols to their corresponding market IDs by querying order books.
/// Thread-safe implementation using SemaphoreSlim for async locking.
/// </summary>
public sealed class MarketResolver : IMarketResolver
{
    private readonly ILighterQueryClient _queryClient;
    private readonly IOptions<TradingBotOptions> _options;
    private readonly ILogger<MarketResolver> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private int _marketId;
    private string _resolvedSymbol = string.Empty;
    private bool _isInitialized;

    /// <summary>
    /// Creates a new MarketResolver instance.
    /// </summary>
    public MarketResolver(
        ILighterQueryClient queryClient,
        IOptions<TradingBotOptions> options,
        ILogger<MarketResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _queryClient = queryClient;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public int MarketId
    {
        get
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException(
                    $"MarketResolver is not initialized. Call {nameof(InitializeAsync)} first.");
            }
            return _marketId;
        }
    }

    /// <inheritdoc />
    public string Symbol => _options.Value.Symbol;

    /// <inheritdoc />
    public string ResolvedSymbol
    {
        get
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException(
                    $"MarketResolver is not initialized. Call {nameof(InitializeAsync)} first.");
            }
            return _resolvedSymbol;
        }
    }

    /// <inheritdoc />
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Quick return if already initialized
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_isInitialized)
            {
                return;
            }

            var configuredSymbol = _options.Value.Symbol;
            if (string.IsNullOrWhiteSpace(configuredSymbol))
            {
                throw new InvalidOperationException(
                    "Symbol is not configured. Please set 'TradingBot:Symbol' in configuration.");
            }

            _logger.LogInformation("Resolving market symbol '{Symbol}'...", configuredSymbol);

            var orderBooks = await _queryClient.GetOrderBooksAsync(cancellationToken);

            if (orderBooks == null || orderBooks.Count == 0)
            {
                throw new InvalidOperationException(
                    "No order books returned from Lighter API. Cannot resolve market symbol.");
            }

            // Log all available markets for debugging
            _logger.LogInformation(
                "Available markets: {Markets}",
                string.Join(", ", orderBooks.Select(ob => $"{ob.Symbol}(ID:{ob.MarketId})")));

            // Priority 1: Exact match (e.g., "BTC-USDC" matches "BTC-USDC")
            var matchingOrderBook = orderBooks.FirstOrDefault(ob =>
                ob.Symbol.Equals(configuredSymbol, StringComparison.OrdinalIgnoreCase));

            // Priority 2: Symbol starts with configured symbol followed by separator
            // (e.g., "BTC" matches "BTC-USDC" but NOT "ETH-BTC")
            matchingOrderBook ??= orderBooks.FirstOrDefault(ob =>
                ob.Symbol.StartsWith(configuredSymbol + "-", StringComparison.OrdinalIgnoreCase));

            // Priority 3: Symbol starts with configured symbol followed by any character
            // (e.g., "BTC" matches "BTCUSDC")
            matchingOrderBook ??= orderBooks.FirstOrDefault(ob =>
                ob.Symbol.StartsWith(configuredSymbol, StringComparison.OrdinalIgnoreCase) &&
                ob.Symbol.Length > configuredSymbol.Length);

            if (matchingOrderBook == null)
            {
                var availableSymbols = string.Join(", ", orderBooks.Select(ob => $"{ob.Symbol}(ID:{ob.MarketId})"));
                throw new InvalidOperationException(
                    $"Symbol '{configuredSymbol}' not found in available markets. " +
                    $"Available symbols: {availableSymbols}. " +
                    $"Tip: Use exact symbol like 'BTC-USDC' or base symbol like 'BTC'.");
            }

            _marketId = matchingOrderBook.MarketId;
            _resolvedSymbol = matchingOrderBook.Symbol;
            _isInitialized = true;

            _logger.LogInformation(
                "Resolved symbol '{Symbol}' to market ID {MarketId} ({FullSymbol})",
                configuredSymbol,
                _marketId,
                _resolvedSymbol);
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}
