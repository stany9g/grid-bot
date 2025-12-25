using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.MoonBag.Services;

namespace GridBot.ApiService.Adapters;

/// <summary>
/// Adapter that bridges MoonBag IMoonBagMarketDataProvider to ApiService's IMarketDataService.
/// </summary>
public sealed class MoonBagMarketDataAdapter : IMoonBagMarketDataProvider
{
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorService _indicatorService;

    public MoonBagMarketDataAdapter(
        IMarketDataService marketDataService,
        IIndicatorService indicatorService)
    {
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(indicatorService);
        _marketDataService = marketDataService;
        _indicatorService = indicatorService;
    }

    public Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default)
    {
        return _marketDataService.GetCurrentPriceAsync(marketId, ct);
    }

    public async Task<IReadOnlyList<decimal>> GetClosingPricesAsync(
        int marketId,
        string interval,
        int count,
        CancellationToken ct = default)
    {
        var candles = await _marketDataService.GetCandlesticksAsync(marketId, interval, count, ct)
            .ConfigureAwait(false);

        return candles.Select(c => c.Close).ToList();
    }

    public decimal CalculateSma(IReadOnlyList<decimal> prices, int period)
    {
        return _indicatorService.CalculateSma(prices, period);
    }
}
