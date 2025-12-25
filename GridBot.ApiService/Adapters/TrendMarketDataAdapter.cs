using GridBot.ApiService.Services.MarketData;
using GridBot.TrendIntelligence.Services;
using TrendCandlestick = GridBot.TrendIntelligence.Models.CandlestickData;

namespace GridBot.ApiService.Adapters;

/// <summary>
/// Adapter that bridges TrendIntelligence IMarketDataProvider to ApiService's IMarketDataService.
/// </summary>
public sealed class TrendMarketDataAdapter : IMarketDataProvider
{
    private readonly IMarketDataService _marketDataService;

    public TrendMarketDataAdapter(IMarketDataService marketDataService)
    {
        ArgumentNullException.ThrowIfNull(marketDataService);
        _marketDataService = marketDataService;
    }

    public async Task<IReadOnlyList<TrendCandlestick>> GetCandlesticksAsync(
        int marketId,
        string interval,
        int count,
        CancellationToken ct = default)
    {
        var candles = await _marketDataService.GetCandlesticksAsync(marketId, interval, count, ct)
            .ConfigureAwait(false);

        return candles.Select(c => new TrendCandlestick
        {
            Timestamp = c.Timestamp,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        }).ToList();
    }

    public Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default)
    {
        return _marketDataService.GetCurrentPriceAsync(marketId, ct);
    }
}
