using System.Globalization;
using GridBot.Abstractions.Scaling;
using Microsoft.Extensions.Logging;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Provides scaling operations for Extended exchange.
/// Extended uses decimal strings in the API, so scaling is primarily for precision handling.
/// </summary>
internal sealed class ExtendedScalingAdapter : IScalingProvider
{
    private readonly ExtendedMarketMapper _marketMapper;
    private readonly ILogger<ExtendedScalingAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedScalingAdapter"/> class.
    /// </summary>
    /// <param name="marketMapper">Market mapper for precision info.</param>
    /// <param name="logger">Logger instance.</param>
    public ExtendedScalingAdapter(
        ExtendedMarketMapper marketMapper,
        ILogger<ExtendedScalingAdapter> logger)
    {
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<MarketScaling> GetMarketScalingAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        var marketInfo = _marketMapper.GetMarketInfo(marketId);
        if (marketInfo == null)
        {
            throw new InvalidOperationException($"Market {marketId} not found. Ensure market mapper is initialized.");
        }

        var priceDecimals = marketInfo.CollateralAssetPrecision;
        var amountDecimals = marketInfo.AssetPrecision;
        var priceScale = (long)Math.Pow(10, priceDecimals);
        var amountScale = (long)Math.Pow(10, amountDecimals);
        var minTickSize = decimal.TryParse(marketInfo.TradingConfig?.MinPriceChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var tick) ? tick : 0.01m;
        var minStepSize = decimal.TryParse(marketInfo.TradingConfig?.MinOrderSizeChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var step) ? step : 0.001m;
        var minOrderSize = decimal.TryParse(marketInfo.TradingConfig?.MinOrderSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var minSize) ? minSize : 0.001m;

        var scaling = new MarketScaling
        {
            MarketId = marketId,
            PriceScale = priceScale,
            AmountScale = amountScale,
            PriceDecimals = priceDecimals,
            AmountDecimals = amountDecimals,
            MinTickSize = minTickSize,
            MinStepSize = minStepSize,
            MinOrderSize = minOrderSize
        };

        return Task.FromResult(scaling);
    }

    /// <inheritdoc />
    public long ScalePrice(decimal price, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Extended uses decimal strings, but we scale to integer for internal consistency
        // Price multiplier = 10^priceDecimals
        var multiplier = (decimal)Math.Pow(10, scaling.PriceDecimals);
        return (long)Math.Round(price * multiplier, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public long ScaleAmount(decimal amount, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        // Round DOWN for amounts to never exceed intended quantity
        return (long)Math.Floor(amount * scaling.AmountScale);
    }

    /// <inheritdoc />
    public decimal UnscalePrice(long scaledPrice, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        var multiplier = (decimal)Math.Pow(10, scaling.PriceDecimals);
        return scaledPrice / multiplier;
    }

    /// <inheritdoc />
    public decimal UnscaleAmount(long scaledAmount, MarketScaling scaling)
    {
        ArgumentNullException.ThrowIfNull(scaling);

        return scaledAmount / (decimal)scaling.AmountScale;
    }

    /// <summary>
    /// Rounds a price to the nearest valid tick for Extended.
    /// Rounds toward less aggressive price (buy down, sell up).
    /// </summary>
    /// <param name="price">The price to round.</param>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="isBuy">Whether this is a buy order.</param>
    /// <returns>The rounded price.</returns>
    public decimal RoundPrice(decimal price, string marketId, bool isBuy)
    {
        var tickSize = _marketMapper.GetTickSize(marketId);
        if (tickSize <= 0)
            return price;

        // Round to tick size, rounding toward less aggressive price
        // Buy: round down, Sell: round up
        if (isBuy)
        {
            return Math.Floor(price / tickSize) * tickSize;
        }
        else
        {
            return Math.Ceiling(price / tickSize) * tickSize;
        }
    }

    /// <summary>
    /// Rounds a quantity to the nearest valid step for Extended.
    /// Always rounds down to never exceed intended quantity.
    /// </summary>
    /// <param name="quantity">The quantity to round.</param>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>The rounded quantity.</returns>
    public decimal RoundQuantity(decimal quantity, string marketId)
    {
        var stepSize = _marketMapper.GetStepSize(marketId);
        if (stepSize <= 0)
            return quantity;

        // Always round down
        return Math.Floor(quantity / stepSize) * stepSize;
    }

    /// <summary>
    /// Formats a price as a string for the Extended API.
    /// </summary>
    /// <param name="price">The price.</param>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Formatted price string.</returns>
    public string FormatPrice(decimal price, string marketId)
    {
        var decimals = _marketMapper.GetPriceDecimals(marketId);
        return price.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a quantity as a string for the Extended API.
    /// </summary>
    /// <param name="quantity">The quantity.</param>
    /// <param name="marketId">The market identifier.</param>
    /// <returns>Formatted quantity string.</returns>
    public string FormatQuantity(decimal quantity, string marketId)
    {
        var decimals = _marketMapper.GetSizeDecimals(marketId);
        return quantity.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }
}
