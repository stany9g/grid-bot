using GridBot.AdvancedRisk.Services;
using GridBot.ApiService.Services.MarketData;

namespace GridBot.ApiService.Adapters;

/// <summary>
/// Adapter that bridges AdvancedRisk IAdvancedRiskMarketDataProvider to ApiService's IMarketDataService.
/// </summary>
public sealed class AdvancedRiskMarketDataAdapter : IAdvancedRiskMarketDataProvider
{
    private readonly IMarketDataService _marketDataService;

    public AdvancedRiskMarketDataAdapter(IMarketDataService marketDataService)
    {
        ArgumentNullException.ThrowIfNull(marketDataService);
        _marketDataService = marketDataService;
    }

    public async Task<decimal?> GetMidPriceAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var orderBook = await _marketDataService.GetOrderBookSnapshotAsync(marketId, 1, ct)
                .ConfigureAwait(false);

            if (orderBook.BestBid > 0 && orderBook.BestAsk > 0)
            {
                return (orderBook.BestBid + orderBook.BestAsk) / 2;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(decimal BidDepthUsd, decimal AskDepthUsd, decimal SpreadBps)?> GetOrderBookDepthAsync(
        int marketId,
        decimal rangePercent,
        CancellationToken ct = default)
    {
        try
        {
            var orderBook = await _marketDataService.GetOrderBookSnapshotAsync(marketId, 50, ct)
                .ConfigureAwait(false);

            if (orderBook.BestBid <= 0 || orderBook.BestAsk <= 0)
            {
                return null;
            }

            var midPrice = (orderBook.BestBid + orderBook.BestAsk) / 2;
            var lowerBound = midPrice * (1 - rangePercent);
            var upperBound = midPrice * (1 + rangePercent);

            decimal bidDepth = 0;
            decimal askDepth = 0;

            foreach (var level in orderBook.Bids)
            {
                if (level.Price >= lowerBound)
                {
                    bidDepth += level.Price * level.Size;
                }
            }

            foreach (var level in orderBook.Asks)
            {
                if (level.Price <= upperBound)
                {
                    askDepth += level.Price * level.Size;
                }
            }

            var spreadBps = (orderBook.BestAsk - orderBook.BestBid) / midPrice * 10000;

            return (bidDepth, askDepth, spreadBps);
        }
        catch
        {
            return null;
        }
    }
}
