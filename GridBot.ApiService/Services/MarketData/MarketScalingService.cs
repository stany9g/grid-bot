using System.Collections.Concurrent;
using System.Globalization;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Service for scaling prices and amounts according to market-specific decimal precision.
/// Caches market metadata for performance.
/// </summary>
public sealed class MarketScalingService : IMarketScalingService
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILogger<MarketScalingService> _logger;
    private readonly ConcurrentDictionary<int, MarketMetadata> _marketCache = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public MarketScalingService(
        ILighterQueryClient queryClient,
        ILogger<MarketScalingService> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<long> ScalePriceAsync(decimal price, int marketId, CancellationToken ct = default)
    {
        if (price < 0)
            throw new ArgumentException($"Price cannot be negative: {price}", nameof(price));

        var metadata = await GetMarketMetadataAsync(marketId, ct).ConfigureAwait(false);
        var multiplier = metadata.PriceMultiplier;
        var scaledPrice = price * multiplier;
        var result = (long)Math.Round(scaledPrice, MidpointRounding.AwayFromZero);

        _logger.LogTrace(
            "Scaled price {Price} to {ScaledPrice} for market {MarketId} (decimals: {Decimals})",
            price, result, marketId, metadata.SupportedPriceDecimals);

        return result;
    }

    /// <inheritdoc />
    public async Task<long> ScaleBaseAmountAsync(decimal amount, int marketId, CancellationToken ct = default)
    {
        if (amount < 0)
            throw new ArgumentException($"Amount cannot be negative: {amount}", nameof(amount));

        var metadata = await GetMarketMetadataAsync(marketId, ct).ConfigureAwait(false);
        var multiplier = metadata.SizeMultiplier;
        var scaledAmount = amount * multiplier;
        var rawResult = (long)Math.Round(scaledAmount, MidpointRounding.AwayFromZero);

        // Snap to valid lot size - all amounts must be multiples of LotSize
        var lotSize = metadata.LotSize;
        long result;
        if (lotSize > 1)
        {
            // Round to nearest lot size (not down, to preserve intent)
            result = ((rawResult + lotSize / 2) / lotSize) * lotSize;

            // If we rounded to zero but had a positive input, use minimum lot size
            if (result == 0 && amount > 0)
            {
                result = lotSize;
            }

            if (result != rawResult)
            {
                _logger.LogWarning(
                    "LOT SIZE SNAP for market {MarketId}: raw={RawResult} -> snapped={Result} (lotSize={LotSize})",
                    marketId, rawResult, result, lotSize);
            }
        }
        else
        {
            result = rawResult;
        }

        // Enforce minimum order size from market metadata
        // MinBaseAmount is human-readable (e.g., 0.002 ETH), scale it for comparison
        var minBaseAmountScaled = (long)Math.Round(metadata.MinBaseAmount * multiplier, MidpointRounding.AwayFromZero);
        if (result > 0 && minBaseAmountScaled > 0 && result < minBaseAmountScaled)
        {
            _logger.LogWarning(
                "MIN ORDER SIZE BUMP for market {MarketId}: {Result} -> {Min} (minBaseAmount={MinBase}, scaled={MinScaled})",
                marketId, result, minBaseAmountScaled, metadata.MinBaseAmount, minBaseAmountScaled);
            result = minBaseAmountScaled;

            // Re-snap to lot size (in case minimum doesn't align with lot size)
            if (lotSize > 1 && result % lotSize != 0)
            {
                result = ((result + lotSize - 1) / lotSize) * lotSize; // Round up to preserve minimum
                _logger.LogDebug(
                    "Re-snapped minimum to lot size for market {MarketId}: {Min} -> {Result}",
                    marketId, minBaseAmountScaled, result);
            }
        }

        // Warn about precision loss
        if (amount > 0 && result == 0)
        {
            _logger.LogWarning(
                "PRECISION LOSS: Amount {Amount} with {Decimals} decimals rounded to ZERO for market {MarketId}. " +
                "This will likely cause issues.",
                amount, metadata.SupportedSizeDecimals, marketId);
        }
        else if (result > 0)
        {
            // Check for significant precision loss
            var convertedBack = (decimal)result / multiplier;
            var precisionLoss = Math.Abs(amount - convertedBack);
            var precisionLossPercentage = amount > 0 ? (precisionLoss / amount) * 100m : 0m;

            if (precisionLossPercentage > 0.1m)
            {
                _logger.LogWarning(
                    "PRECISION LOSS: Amount {Amount} has {LossPercentage:F4}% precision loss for market {MarketId}. " +
                    "Original: {Original}, Scaled: {Scaled}, Back to decimal: {ConvertedBack}",
                    amount, precisionLossPercentage, marketId, amount, result, convertedBack);
            }
        }

        // Log scaled values at Debug level for troubleshooting
        _logger.LogDebug(
            "Scaling amount for market {MarketId}: input={Amount}, decimals={Decimals}, multiplier={Multiplier}, " +
            "scaled={Scaled}, lotSize={LotSize}, minBase={MinBase}",
            marketId, amount, metadata.SupportedSizeDecimals, multiplier, result, lotSize, metadata.MinBaseAmount);

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal> UnscalePriceAsync(long scaledPrice, int marketId, CancellationToken ct = default)
    {
        var metadata = await GetMarketMetadataAsync(marketId, ct).ConfigureAwait(false);
        return scaledPrice / metadata.PriceMultiplier;
    }

    /// <inheritdoc />
    public async Task<decimal> UnscaleBaseAmountAsync(long scaledAmount, int marketId, CancellationToken ct = default)
    {
        var metadata = await GetMarketMetadataAsync(marketId, ct).ConfigureAwait(false);
        return scaledAmount / metadata.SizeMultiplier;
    }

    /// <inheritdoc />
    public async Task<MarketMetadata> GetMarketMetadataAsync(int marketId, CancellationToken ct = default)
    {
        if (_marketCache.TryGetValue(marketId, out var cached))
            return cached;

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_marketCache.TryGetValue(marketId, out cached))
                return cached;

            var details = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: ct)
                .ConfigureAwait(false);

            var metadata = new MarketMetadata
            {
                MarketId = details.MarketId,
                Symbol = details.Symbol,
                SupportedPriceDecimals = details.SupportedPriceDecimals,
                SupportedSizeDecimals = details.SupportedSizeDecimals,
                SizeDecimals = details.SizeDecimals,
                MinBaseAmount = decimal.TryParse(details.MinBaseAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var minBase) ? minBase : 0,
                MinQuoteAmount = decimal.TryParse(details.MinQuoteAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var minQuote) ? minQuote : 0
            };

            _marketCache[marketId] = metadata;

            _logger.LogWarning(
                "MARKET METADATA for {Symbol} (ID: {MarketId}): " +
                "SupportedPriceDecimals={SupportedPriceDecimals}, SupportedSizeDecimals={SupportedSizeDecimals}, " +
                "SizeDecimals={SizeDecimals}, LotSize={LotSize}, MinBase={MinBase}, MinQuote={MinQuote}",
                metadata.Symbol, marketId,
                metadata.SupportedPriceDecimals, metadata.SupportedSizeDecimals,
                metadata.SizeDecimals, metadata.LotSize, metadata.MinBaseAmount, metadata.MinQuoteAmount);

            return metadata;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task PreloadMarketsAsync(CancellationToken ct = default)
    {
        try
        {
            var orderBooks = await _queryClient.GetOrderBooksAsync(ct).ConfigureAwait(false);

            foreach (var book in orderBooks)
            {
                var metadata = new MarketMetadata
                {
                    MarketId = book.MarketId,
                    Symbol = book.Symbol,
                    SupportedPriceDecimals = book.SupportedPriceDecimals,
                    SupportedSizeDecimals = book.SupportedSizeDecimals,
                    SizeDecimals = book.SizeDecimals,
                    MinBaseAmount = decimal.TryParse(book.MinBaseAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var minBase) ? minBase : 0,
                    MinQuoteAmount = decimal.TryParse(book.MinQuoteAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var minQuote) ? minQuote : 0
                };

                _marketCache[book.MarketId] = metadata;

                _logger.LogDebug(
                    "Preloaded market {Symbol} (ID: {MarketId}): PriceDecimals={PriceDecimals}, SizeDecimals={SizeDecimals}",
                    metadata.Symbol, book.MarketId,
                    metadata.SupportedPriceDecimals, metadata.SupportedSizeDecimals);
            }

            _logger.LogInformation("Preloaded {Count} markets", orderBooks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preload markets, will load on demand");
        }
    }
}
