using GridBot.Abstractions.Trading;
using GridBot.Core.Services.Configuration;
using Microsoft.Extensions.Logging;

using AbstractionsMarketInfo = GridBot.Abstractions.Models.Market.MarketInfo;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Resolves market symbols to market IDs via the exchange API.
/// </summary>
public sealed class MarketResolver : IMarketResolver
{
    private readonly IMarketDataClient _marketDataClient;
    private readonly IGridConfigurationService _configService;
    private readonly ILogger<MarketResolver> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private int _marketId;
    private string _resolvedSymbol = string.Empty;
    private bool _isInitialized;

    // Cache for available markets
    private List<MarketInfo>? _cachedMarkets;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public MarketResolver(
        IMarketDataClient marketDataClient,
        IGridConfigurationService configService,
        ILogger<MarketResolver> logger)
    {
        _marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int MarketId
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MarketResolver is not initialized. Call InitializeAsync first.");
            return _marketId;
        }
    }

    public string Symbol => _configService.Current.Market;

    public string ResolvedSymbol
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MarketResolver is not initialized. Call InitializeAsync first.");
            return _resolvedSymbol;
        }
    }

    public bool IsInitialized => _isInitialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;

            var symbol = _configService.Current.Market;
            var resolvedId = await ResolveMarketIndexAsync(symbol, cancellationToken).ConfigureAwait(false);

            if (resolvedId.HasValue)
            {
                _marketId = resolvedId.Value;
                _resolvedSymbol = $"{symbol}-USDC";
                _isInitialized = true;

                // Update config with resolved market index
                await _configService.UpdateAsync(config =>
                {
                    config.MarketIndex = _marketId;
                }, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Resolved market {Symbol} to ID {MarketId}", _resolvedSymbol, _marketId);
            }
            else
            {
                _logger.LogWarning("Could not resolve market {Symbol}, using default index from config", symbol);
                _marketId = _configService.Current.MarketIndex;
                _resolvedSymbol = $"{symbol}-USDC";
                _isInitialized = true;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<int?> ResolveMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var markets = await GetAvailableMarketsAsync(cancellationToken).ConfigureAwait(false);

        // Match by base asset (e.g., "BTC" matches "BTC-USDC")
        var normalized = symbol.ToUpperInvariant().Trim();
        var match = markets.FirstOrDefault(m =>
            m.BaseAsset.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            m.Symbol.StartsWith(normalized + "-", StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            _logger.LogDebug("Resolved symbol {Symbol} to market ID {MarketId}", symbol, match.MarketId);
            return match.MarketId;
        }

        _logger.LogWarning("Could not find market for symbol {Symbol}", symbol);
        return null;
    }

    public async Task<IReadOnlyList<MarketInfo>> GetAvailableMarketsAsync(CancellationToken cancellationToken = default)
    {
        // Return cached if valid
        if (_cachedMarkets is not null && DateTimeOffset.UtcNow < _cacheExpiry)
        {
            return _cachedMarkets;
        }

        try
        {
            var markets = await _marketDataClient.GetMarketsAsync(cancellationToken).ConfigureAwait(false);

            _cachedMarkets = markets
                .Select(m => new MarketInfo(
                    MarketId: ParseMarketId(m.MarketId),
                    Symbol: m.Symbol,
                    BaseAsset: m.BaseAsset,
                    IsActive: m.IsActive))
                .ToList();

            _cacheExpiry = DateTimeOffset.UtcNow + CacheDuration;

            _logger.LogDebug("Cached {Count} markets from exchange", _cachedMarkets.Count);
            return _cachedMarkets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch available markets");
            return _cachedMarkets ?? [];
        }
    }

    private static int ParseMarketId(string marketId)
    {
        return int.TryParse(marketId, out var id) ? id : 0;
    }

    private static string ExtractBaseAsset(string symbol)
    {
        // "BTC-USDC" -> "BTC"
        var dashIndex = symbol.IndexOf('-');
        return dashIndex > 0 ? symbol[..dashIndex] : symbol;
    }
}
