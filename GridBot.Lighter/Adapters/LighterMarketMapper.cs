using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Maps between string marketId (abstraction layer) and int marketId (Lighter-specific).
/// Caches market metadata from the Lighter API for efficient lookups.
/// Thread-safe for concurrent access.
/// </summary>
internal sealed class LighterMarketMapper
{
    private readonly Dictionary<string, int> _symbolToId = new();
    private readonly Dictionary<int, string> _idToSymbol = new();
    private readonly Dictionary<int, OrderBook> _marketMetadata = new();
    private readonly object _lock = new();
    private readonly ILogger<LighterMarketMapper> _logger;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterMarketMapper"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public LighterMarketMapper(ILogger<LighterMarketMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets whether the mapper has been initialized with market data.
    /// </summary>
    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _initialized;
            }
        }
    }

    /// <summary>
    /// Initializes the mapper with market data from the Lighter API.
    /// </summary>
    /// <param name="client">The Lighter query client.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(ILighterQueryClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            if (_initialized) return;
        }

        _logger.LogInformation("Initializing market mapper from Lighter API");

        var markets = await client.GetOrderBooksAsync(ct);

        lock (_lock)
        {
            foreach (var market in markets)
            {
                _symbolToId[market.Symbol] = market.MarketId;
                _idToSymbol[market.MarketId] = market.Symbol;
                _marketMetadata[market.MarketId] = market;

                _logger.LogDebug(
                    "Registered market: {Symbol} -> ID {MarketId}",
                    market.Symbol, market.MarketId);
            }

            _initialized = true;
        }

        _logger.LogInformation("Market mapper initialized with {Count} markets", markets.Count);
    }

    /// <summary>
    /// Initializes the mapper with pre-loaded market data.
    /// </summary>
    /// <param name="markets">List of order books from the API.</param>
    public void Initialize(IReadOnlyList<OrderBook> markets)
    {
        ArgumentNullException.ThrowIfNull(markets);

        lock (_lock)
        {
            if (_initialized) return;

            foreach (var market in markets)
            {
                _symbolToId[market.Symbol] = market.MarketId;
                _idToSymbol[market.MarketId] = market.Symbol;
                _marketMetadata[market.MarketId] = market;
            }

            _initialized = true;
        }

        _logger.LogInformation("Market mapper initialized with {Count} markets", markets.Count);
    }

    /// <summary>
    /// Converts a string market ID (symbol or numeric) to a Lighter integer market ID.
    /// </summary>
    /// <param name="marketId">The string market identifier (e.g., "BTC-USDC" or "0").</param>
    /// <returns>The Lighter integer market ID.</returns>
    /// <exception cref="ArgumentException">Thrown if the market ID is unknown.</exception>
    public int ToLighterId(string marketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        lock (_lock)
        {
            // First try symbol lookup
            if (_symbolToId.TryGetValue(marketId, out var id))
            {
                return id;
            }

            // Fall back to parsing as integer
            if (int.TryParse(marketId, out var numericId))
            {
                return numericId;
            }

            throw new ArgumentException($"Unknown market ID: {marketId}", nameof(marketId));
        }
    }

    /// <summary>
    /// Converts a Lighter integer market ID to a string symbol.
    /// </summary>
    /// <param name="marketId">The Lighter integer market ID.</param>
    /// <returns>The market symbol (e.g., "BTC-USDC") or the ID as string if unknown.</returns>
    public string FromLighterId(int marketId)
    {
        lock (_lock)
        {
            return _idToSymbol.TryGetValue(marketId, out var symbol) ? symbol : marketId.ToString();
        }
    }

    /// <summary>
    /// Gets the market metadata for a given market ID.
    /// </summary>
    /// <param name="marketId">The Lighter integer market ID.</param>
    /// <returns>The market metadata, or null if not found.</returns>
    public OrderBook? GetMarketMetadata(int marketId)
    {
        lock (_lock)
        {
            return _marketMetadata.TryGetValue(marketId, out var metadata) ? metadata : null;
        }
    }

    /// <summary>
    /// Gets the market metadata for a given string market ID.
    /// </summary>
    /// <param name="marketId">The string market identifier.</param>
    /// <returns>The market metadata, or null if not found.</returns>
    public OrderBook? GetMarketMetadata(string marketId)
    {
        var id = ToLighterId(marketId);
        return GetMarketMetadata(id);
    }

    /// <summary>
    /// Gets all registered market IDs as strings.
    /// </summary>
    /// <returns>Collection of market symbols.</returns>
    public IReadOnlyCollection<string> GetAllMarketIds()
    {
        lock (_lock)
        {
            return _symbolToId.Keys.ToList();
        }
    }

    /// <summary>
    /// Gets all market metadata.
    /// </summary>
    /// <returns>Collection of all market metadata.</returns>
    public IReadOnlyCollection<OrderBook> GetAllMarkets()
    {
        lock (_lock)
        {
            return _marketMetadata.Values.ToList();
        }
    }
}
