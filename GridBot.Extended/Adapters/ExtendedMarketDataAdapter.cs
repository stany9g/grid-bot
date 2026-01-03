using System.Globalization;
using GridBot.Abstractions.Models.Market;
using GridBot.Abstractions.Models.OrderBook;
using GridBot.Abstractions.Trading;
using Microsoft.Extensions.Logging;
using ApiMarketInfo = GridBot.Extended.Models.Api.MarketInfo;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Adapts Extended HTTP client to the <see cref="IMarketDataClient"/> interface.
/// </summary>
internal sealed class ExtendedMarketDataAdapter : IMarketDataClient
{
    private readonly IExtendedHttpClient _httpClient;
    private readonly ExtendedMarketMapper _marketMapper;
    private readonly ILogger<ExtendedMarketDataAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedMarketDataAdapter"/> class.
    /// </summary>
    /// <param name="httpClient">Extended HTTP client.</param>
    /// <param name="marketMapper">Market mapper.</param>
    /// <param name="logger">Logger instance.</param>
    public ExtendedMarketDataAdapter(
        IExtendedHttpClient httpClient,
        ExtendedMarketMapper marketMapper,
        ILogger<ExtendedMarketDataAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<decimal> GetCurrentPriceAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        var stats = await _httpClient.GetMarketStatsAsync(marketId, ct);

        // Try to parse mark price first, then best bid/ask mid
        if (decimal.TryParse(stats.MarkPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var markPrice))
        {
            return markPrice;
        }

        // Fallback to mid-price from best bid/ask
        if (decimal.TryParse(stats.BestBid, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) &&
            decimal.TryParse(stats.BestAsk, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask))
        {
            return (bid + ask) / 2m;
        }

        throw new InvalidOperationException($"Unable to get price for market {marketId}");
    }

    /// <inheritdoc />
    public async Task<OrderBookSnapshot> GetOrderBookAsync(string marketId, int depth = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        var response = await _httpClient.GetOrderBookAsync(marketId, depth, ct);

        var bids = response.Bids
            .Select(b => new PriceLevel
            {
                Price = decimal.TryParse(b.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m,
                Quantity = decimal.TryParse(b.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 0m
            })
            .Where(x => x.Price > 0 && x.Quantity > 0)
            .OrderByDescending(x => x.Price)
            .ToList();

        var asks = response.Asks
            .Select(a => new PriceLevel
            {
                Price = decimal.TryParse(a.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m,
                Quantity = decimal.TryParse(a.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 0m
            })
            .Where(x => x.Price > 0 && x.Quantity > 0)
            .OrderBy(x => x.Price)
            .ToList();

        var bestBidPrice = bids.Count > 0 ? bids[0].Price : 0m;
        var bestAskPrice = asks.Count > 0 ? asks[0].Price : 0m;
        var spread = bestAskPrice - bestBidPrice;

        return new OrderBookSnapshot
        {
            MarketId = marketId,
            BestBidPrice = bestBidPrice,
            BestAskPrice = bestAskPrice,
            Spread = spread,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(response.Timestamp),
            Bids = bids,
            Asks = asks
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CandlestickData>> GetCandlesticksAsync(
        string marketId,
        string resolution,
        int count,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution);

        // Map resolution to Extended format
        var interval = MapResolution(resolution);

        var candles = await _httpClient.GetCandlesAsync(marketId, "trades", interval, count, ct);

        return candles.Select(c => new CandlestickData
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
            Open = decimal.TryParse(c.Open, NumberStyles.Any, CultureInfo.InvariantCulture, out var o) ? o : 0m,
            High = decimal.TryParse(c.High, NumberStyles.Any, CultureInfo.InvariantCulture, out var h) ? h : 0m,
            Low = decimal.TryParse(c.Low, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0m,
            Close = decimal.TryParse(c.Close, NumberStyles.Any, CultureInfo.InvariantCulture, out var cl) ? cl : 0m,
            Volume = decimal.TryParse(c.Volume, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default)
    {
        var markets = await _httpClient.GetMarketsAsync(ct);

        return markets.Select(m =>
        {
            var minOrderSize = decimal.TryParse(m.TradingConfig?.MinOrderSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var min) ? min : 0.001m;
            var tickSize = decimal.TryParse(m.TradingConfig?.MinPriceChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var tick) ? tick : 0.01m;
            var stepSize = decimal.TryParse(m.TradingConfig?.MinOrderSizeChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var step) ? step : 0.001m;
            var maxLeverage = decimal.TryParse(m.TradingConfig?.MaxLeverage, NumberStyles.Any, CultureInfo.InvariantCulture, out var lev) ? (int)lev : 20;

            return new MarketInfo
            {
                MarketId = m.Name,
                Symbol = $"{m.AssetName}/{m.CollateralAssetName}",
                BaseAsset = m.AssetName,
                QuoteAsset = m.CollateralAssetName,
                MinOrderSize = minOrderSize,
                TickSize = tickSize,
                StepSize = stepSize,
                MaxLeverage = maxLeverage,
                IsActive = m.Active
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<FundingRateInfo?> GetFundingRateAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        try
        {
            var funding = await _httpClient.GetFundingRateAsync(marketId, ct);

            var fundingRate = decimal.TryParse(funding.FundingRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0m;
            var predictedRate = funding.NextFundingRate != null &&
                decimal.TryParse(funding.NextFundingRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var pr)
                ? pr : (decimal?)null;

            return new FundingRateInfo
            {
                MarketId = marketId,
                FundingRate = fundingRate,
                NextFundingTime = funding.NextFundingTime.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(funding.NextFundingTime.Value)
                    : DateTimeOffset.UtcNow.AddHours(8),
                PredictedRate = predictedRate,
                FundingIntervalHours = funding.FundingIntervalHours ?? 8
            };
        }
        catch (ExtendedApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Market may not be a perpetual
            return null;
        }
    }

    private static string MapResolution(string resolution)
    {
        // Map common resolution formats to Extended's ISO 8601 interval format
        return resolution.ToLowerInvariant() switch
        {
            "1m" => "PT1M",
            "5m" => "PT5M",
            "15m" => "PT15M",
            "30m" => "PT30M",
            "1h" => "PT1H",
            "2h" => "PT2H",
            "4h" => "PT4H",
            "8h" => "PT8H",
            "12h" => "PT12H",
            "1d" or "24h" => "PT24H",
            "1w" or "7d" => "P7D",
            "1M" or "30d" => "P30D",
            _ when resolution.StartsWith("PT") || resolution.StartsWith("P") => resolution, // Already in ISO format
            _ => "PT1H" // Default to 1 hour
        };
    }
}
