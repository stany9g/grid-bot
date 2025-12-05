using System.Globalization;
using GridBot.ApiService.Models.Trading;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Implementation of market data service using Lighter DEX API.
/// Thread-safe singleton service.
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILogger<MarketDataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketDataService"/> class.
    /// </summary>
    /// <param name="queryClient">Lighter query client.</param>
    /// <param name="logger">Logger instance.</param>
    public MarketDataService(ILighterQueryClient queryClient, ILogger<MarketDataService> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get market metadata for last trade price
            var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);

            // Use last trade price if available (now a decimal in the API response)
            if (marketData.LastTradePrice > 0)
            {
                return marketData.LastTradePrice;
            }

            // Fall back to mid price from best bid/ask
            var orderBook = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: 1, cancellationToken);
            var bestBid = orderBook.Bids.FirstOrDefault();
            var bestAsk = orderBook.Asks.FirstOrDefault();

            if (bestBid != null && bestAsk != null)
            {
                var bidPrice = ParseDecimal(bestBid.Price);
                var askPrice = ParseDecimal(bestAsk.Price);
                return (bidPrice + askPrice) / 2m;
            }

            _logger.LogWarning("No price data available for market {MarketId}", marketId);
            return 0m;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current price for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId,
        string resolution,
        int count,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candles = await _queryClient.GetCandlesticksAsync(marketId, resolution, count, cancellationToken);

            return candles.Select(c => new CandlestickData
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get candlesticks for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(
        int marketId,
        int depth = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get order book orders (bids and asks)
            var orderBookOrders = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: depth, cancellationToken);

            // Convert OrderBookOrder to aggregated PriceLevel (group by price, sum sizes)
            var bids = orderBookOrders.Bids
                .GroupBy(b => ParseDecimal(b.Price))
                .Select(g => new PriceLevel
                {
                    Price = g.Key,
                    Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount))
                })
                .OrderByDescending(p => p.Price)
                .ToList();

            var asks = orderBookOrders.Asks
                .GroupBy(a => ParseDecimal(a.Price))
                .Select(g => new PriceLevel
                {
                    Price = g.Key,
                    Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount))
                })
                .OrderBy(p => p.Price)
                .ToList();

            var bestBid = bids.FirstOrDefault()?.Price ?? 0m;
            var bestAsk = asks.FirstOrDefault()?.Price ?? 0m;
            var spread = bestAsk > 0 && bestBid > 0 ? bestAsk - bestBid : 0m;

            // Get market metadata for last trade price
            var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);
            var lastPrice = marketData.LastTradePrice;

            if (lastPrice == 0m && bestBid > 0 && bestAsk > 0)
            {
                lastPrice = (bestBid + bestAsk) / 2m;
            }

            return new OrderBookSnapshot
            {
                MarketId = marketId,
                Timestamp = DateTimeOffset.UtcNow, // Use current time since orderBookOrders doesn't have timestamp
                LastPrice = lastPrice,
                BestBid = bestBid,
                BestAsk = bestAsk,
                Spread = spread,
                TotalBidDepth = CalculateTotalDepth(bids),
                TotalAskDepth = CalculateTotalDepth(asks),
                Bids = bids,
                Asks = asks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order book snapshot for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fundingRates = await _queryClient.GetFundingRatesAsync(cancellationToken);

            // Find the Lighter exchange funding rate for this market
            var rate = fundingRates.FirstOrDefault(f =>
                f.MarketId == marketId &&
                f.Exchange.Equals("lighter", StringComparison.OrdinalIgnoreCase));

            if (rate == null)
            {
                _logger.LogDebug("No funding rate found for market {MarketId}", marketId);
                return null;
            }

            if (decimal.TryParse(rate.Rate, NumberStyles.Any, CultureInfo.InvariantCulture, out var fundingRate))
            {
                return fundingRate;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get funding rate for market {MarketId}", marketId);
            return null;
        }
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static decimal CalculateTotalDepth(List<PriceLevel> levels)
    {
        return levels.Sum(l => l.Price * l.Size);
    }
}
