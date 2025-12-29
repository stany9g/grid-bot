using System.Collections.Concurrent;
using GridBot.Abstractions.Scaling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Service for scaling prices and amounts according to market-specific decimal precision.
/// Delegates to the abstraction layer's IScalingProvider for core scaling operations.
/// Uses IServiceScopeFactory to resolve scoped dependencies (IScalingProvider)
/// since this is a singleton service supporting network switching.
/// </summary>
public sealed class MarketScalingService : IMarketScalingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketScalingService> _logger;
    private readonly ConcurrentDictionary<int, MarketMetadata> _metadataCache = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public MarketScalingService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketScalingService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<long> ScalePriceAsync(decimal price, int marketId, CancellationToken ct = default)
    {
        if (price < 0)
            throw new ArgumentException($"Price cannot be negative: {price}", nameof(price));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();

        var marketIdStr = marketId.ToString();
        var scaling = await scalingProvider.GetMarketScalingAsync(marketIdStr, ct).ConfigureAwait(false);
        var result = scalingProvider.ScalePrice(price, scaling);

        _logger.LogTrace(
            "Scaled price {Price} to {ScaledPrice} for market {MarketId} (decimals: {Decimals})",
            price, result, marketId, scaling.PriceDecimals);

        return result;
    }

    /// <inheritdoc />
    public async Task<long> ScaleBaseAmountAsync(decimal amount, int marketId, CancellationToken ct = default)
    {
        if (amount < 0)
            throw new ArgumentException($"Amount cannot be negative: {amount}", nameof(amount));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();

        var marketIdStr = marketId.ToString();
        var scaling = await scalingProvider.GetMarketScalingAsync(marketIdStr, ct).ConfigureAwait(false);
        var result = scalingProvider.ScaleAmount(amount, scaling);

        // Additional logging for debugging
        _logger.LogDebug(
            "Scaling amount for market {MarketId}: input={Amount}, decimals={Decimals}, " +
            "scaled={Scaled}, minStep={MinStep}, minOrder={MinOrder}",
            marketId, amount, scaling.AmountDecimals, result, scaling.MinStepSize, scaling.MinOrderSize);

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal> UnscalePriceAsync(long scaledPrice, int marketId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();

        var marketIdStr = marketId.ToString();
        var scaling = await scalingProvider.GetMarketScalingAsync(marketIdStr, ct).ConfigureAwait(false);
        return scalingProvider.UnscalePrice(scaledPrice, scaling);
    }

    /// <inheritdoc />
    public async Task<decimal> UnscaleBaseAmountAsync(long scaledAmount, int marketId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();

        var marketIdStr = marketId.ToString();
        var scaling = await scalingProvider.GetMarketScalingAsync(marketIdStr, ct).ConfigureAwait(false);
        return scalingProvider.UnscaleAmount(scaledAmount, scaling);
    }

    /// <inheritdoc />
    public async Task<MarketMetadata> GetMarketMetadataAsync(int marketId, CancellationToken ct = default)
    {
        if (_metadataCache.TryGetValue(marketId, out var cached))
            return cached;

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_metadataCache.TryGetValue(marketId, out cached))
                return cached;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();

            var marketIdStr = marketId.ToString();
            var scaling = await scalingProvider.GetMarketScalingAsync(marketIdStr, ct).ConfigureAwait(false);

            var metadata = new MarketMetadata
            {
                MarketId = marketId,
                Symbol = scaling.MarketId,
                SupportedPriceDecimals = scaling.PriceDecimals,
                SupportedSizeDecimals = scaling.AmountDecimals,
                SizeDecimals = scaling.AmountDecimals,
                MinBaseAmount = scaling.MinOrderSize,
                MinQuoteAmount = 0
            };

            _metadataCache[marketId] = metadata;

            _logger.LogInformation(
                "MARKET METADATA for {Symbol} (ID: {MarketId}): " +
                "PriceDecimals={PriceDecimals}, SizeDecimals={SizeDecimals}, " +
                "MinStep={MinStep}, MinOrder={MinOrder}",
                metadata.Symbol, marketId,
                metadata.SupportedPriceDecimals, metadata.SupportedSizeDecimals,
                scaling.MinStepSize, scaling.MinOrderSize);

            return metadata;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public Task PreloadMarketsAsync(CancellationToken ct = default)
    {
        // With the abstraction layer, markets are loaded on demand via IScalingProvider.
        // This method is kept for API compatibility but does nothing.
        _logger.LogDebug("PreloadMarketsAsync is a no-op with abstraction layer; markets loaded on demand");
        return Task.CompletedTask;
    }
}
