using GridBot.Abstractions.Scaling;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts Lighter market scaling to the <see cref="IScalingProvider"/> interface.
/// Caches scaling information from market metadata for efficient conversions.
/// </summary>
internal sealed class LighterScalingAdapter : IScalingProvider
{
    private readonly LighterMarketMapper _marketMapper;
    private readonly ILogger<LighterScalingAdapter> _logger;
    private readonly Dictionary<string, MarketScaling> _scalingCache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterScalingAdapter"/> class.
    /// </summary>
    /// <param name="marketMapper">The market mapper with market metadata.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterScalingAdapter(
        LighterMarketMapper marketMapper,
        ILogger<LighterScalingAdapter> logger)
    {
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<MarketScaling> GetMarketScalingAsync(string marketId, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_scalingCache.TryGetValue(marketId, out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        var metadata = _marketMapper.GetMarketMetadata(marketId);
        if (metadata == null)
        {
            _logger.LogWarning("No market metadata found for {MarketId}, using defaults", marketId);

            // Return default Lighter scaling (2 decimal price, 8 decimal size)
            var defaultScaling = new MarketScaling
            {
                MarketId = marketId,
                PriceScale = 100, // 2 decimals
                AmountScale = 100_000_000, // 8 decimals
                PriceDecimals = 2,
                AmountDecimals = 8,
                MinTickSize = 0.01m,
                MinStepSize = 0.00000001m,
                MinOrderSize = 0.001m
            };

            return Task.FromResult(defaultScaling);
        }

        // Lighter uses:
        // - PriceDecimals: number of decimals for price display
        // - SizeDecimals: number of decimals for size display
        // - SupportedPriceDecimals/SupportedSizeDecimals: precision supported by the exchange

        var priceDecimals = metadata.PriceDecimals;
        var sizeDecimals = metadata.SizeDecimals;

        // For Lighter, the price is stored as integer with PriceDecimals implied
        // Price scale = 10^PriceDecimals (e.g., 2 decimals = 100)
        var priceScale = (long)Math.Pow(10, priceDecimals);

        // For size, Lighter uses 8 decimal places internally (SupportedSizeDecimals)
        // but displays with SizeDecimals
        var amountScale = (long)Math.Pow(10, metadata.SupportedSizeDecimals);

        var scaling = new MarketScaling
        {
            MarketId = marketId,
            PriceScale = priceScale,
            AmountScale = amountScale,
            PriceDecimals = priceDecimals,
            AmountDecimals = metadata.SupportedSizeDecimals,
            MinTickSize = 1m / priceScale,
            MinStepSize = 1m / amountScale,
            MinOrderSize = decimal.TryParse(metadata.MinBaseAmount, out var minBase) ? minBase : 0.001m,
            MaxOrderSize = decimal.TryParse(metadata.MaxBaseAmount, out var maxBase) && maxBase > 0 ? maxBase : null
        };

        lock (_cacheLock)
        {
            _scalingCache[marketId] = scaling;
        }

        _logger.LogDebug(
            "Cached scaling for {MarketId}: PriceScale={PriceScale}, AmountScale={AmountScale}",
            marketId, priceScale, amountScale);

        return Task.FromResult(scaling);
    }

    /// <inheritdoc />
    public long ScalePrice(decimal price, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Convert decimal price to scaled integer
        // e.g., price=2920.57, scale=100 -> 292057
        return (long)Math.Round(price * scaling.PriceScale, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public long ScaleAmount(decimal amount, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Convert decimal amount to scaled integer
        // e.g., amount=0.01, scale=100000000 -> 1000000
        return (long)Math.Round(amount * scaling.AmountScale, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public decimal UnscalePrice(long scaledPrice, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Convert scaled integer back to decimal price
        // e.g., scaledPrice=292057, scale=100 -> 2920.57
        return scaledPrice / (decimal)scaling.PriceScale;
    }

    /// <inheritdoc />
    public decimal UnscaleAmount(long scaledAmount, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Convert scaled integer back to decimal amount
        // e.g., scaledAmount=1000000, scale=100000000 -> 0.01
        return scaledAmount / (decimal)scaling.AmountScale;
    }

    /// <summary>
    /// Clears the scaling cache. Useful when market metadata is updated.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _scalingCache.Clear();
        }

        _logger.LogDebug("Scaling cache cleared");
    }
}
