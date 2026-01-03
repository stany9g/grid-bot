using System.Collections.Concurrent;
using System.Globalization;
using GridBot.Extended.Models.Api;
using Microsoft.Extensions.Logging;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Maps and caches market information for the Extended exchange.
/// Extended uses string market IDs directly, so no numeric mapping is needed.
/// </summary>
internal sealed class ExtendedMarketMapper
{
    private readonly ILogger<ExtendedMarketMapper> _logger;
    private readonly ConcurrentDictionary<string, MarketInfo> _marketCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedMarketMapper"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ExtendedMarketMapper(ILogger<ExtendedMarketMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets whether the mapper is initialized with market data.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets all cached market IDs.
    /// </summary>
    /// <returns>List of market IDs.</returns>
    public IReadOnlyList<string> GetAllMarketIds()
    {
        return _marketCache.Keys.ToList();
    }

    /// <summary>
    /// Gets market info by market ID.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Market info if found, null otherwise.</returns>
    public MarketInfo? GetMarketInfo(string marketId)
    {
        return _marketCache.TryGetValue(marketId, out var info) ? info : null;
    }

    /// <summary>
    /// Gets the tick size (price precision) for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The tick size, or 0.01 as default.</returns>
    public decimal GetTickSize(string marketId)
    {
        var info = GetMarketInfo(marketId);
        var minPriceChange = info?.TradingConfig?.MinPriceChange;
        return minPriceChange != null && decimal.TryParse(minPriceChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var tickSize)
            ? tickSize
            : 0.01m;
    }

    /// <summary>
    /// Gets the step size (quantity precision) for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The step size, or 0.001 as default.</returns>
    public decimal GetStepSize(string marketId)
    {
        var info = GetMarketInfo(marketId);
        var minOrderSizeChange = info?.TradingConfig?.MinOrderSizeChange;
        return minOrderSizeChange != null && decimal.TryParse(minOrderSizeChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var stepSize)
            ? stepSize
            : 0.001m;
    }

    /// <summary>
    /// Gets the minimum order size for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The minimum order size, or 0.001 as default.</returns>
    public decimal GetMinOrderSize(string marketId)
    {
        var info = GetMarketInfo(marketId);
        var minOrderSize = info?.TradingConfig?.MinOrderSize;
        return minOrderSize != null && decimal.TryParse(minOrderSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var minSize)
            ? minSize
            : 0.001m;
    }

    /// <summary>
    /// Initializes the mapper with market data from the API.
    /// </summary>
    /// <param name="httpClient">HTTP client to fetch markets.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(IExtendedHttpClient httpClient, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _logger.LogInformation("Initializing market mapper...");

        var markets = await httpClient.GetMarketsAsync(ct);

        _marketCache.Clear();
        foreach (var market in markets)
        {
            _marketCache[market.Name] = market;
        }

        _isInitialized = true;

        _logger.LogInformation(
            "Market mapper initialized with {Count} markets: {Markets}",
            markets.Count,
            string.Join(", ", markets.Select(m => m.Name).Take(10)) + (markets.Count > 10 ? "..." : ""));
    }

    /// <summary>
    /// Updates a single market's cached info.
    /// </summary>
    /// <param name="market">The market info to update.</param>
    public void UpdateMarket(MarketInfo market)
    {
        ArgumentNullException.ThrowIfNull(market);
        _marketCache[market.Name] = market;
    }

    /// <summary>
    /// Validates that a market exists and is active.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>True if the market is valid and active.</returns>
    public bool IsValidMarket(string marketId)
    {
        var info = GetMarketInfo(marketId);
        return info?.Active == true;
    }

    /// <summary>
    /// Gets the number of price decimals for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Number of price decimals.</returns>
    public int GetPriceDecimals(string marketId)
    {
        var info = GetMarketInfo(marketId);
        // Use collateral asset precision for price decimals
        return info?.CollateralAssetPrecision ?? 2;
    }

    /// <summary>
    /// Gets the number of size decimals for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Number of size decimals.</returns>
    public int GetSizeDecimals(string marketId)
    {
        var info = GetMarketInfo(marketId);
        // Use asset precision for size decimals
        return info?.AssetPrecision ?? 4;
    }

    /// <summary>
    /// Gets the maximum leverage for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Maximum leverage, or 20 as default.</returns>
    public int GetMaxLeverage(string marketId)
    {
        var info = GetMarketInfo(marketId);
        var maxLeverageStr = info?.TradingConfig?.MaxLeverage;
        if (maxLeverageStr != null && decimal.TryParse(maxLeverageStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxLeverage))
        {
            return (int)maxLeverage;
        }
        return 20;
    }
}
