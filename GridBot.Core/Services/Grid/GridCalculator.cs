using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Options;

namespace GridBot.Core.Services.Grid;

/// <summary>
/// Calculates grid levels with fixed spacing.
/// Simple implementation - no ATR-based dynamics.
/// </summary>
public sealed class GridCalculator : IGridCalculator
{
    private readonly SimpleGridConfig _config;
    private long _nextClientOrderIndex;

    public GridCalculator(IOptions<SimpleGridConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config.Value;
        _nextClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <inheritdoc />
    public List<GridLevel> CalculateLevels(decimal centerPrice)
    {
        var levels = new List<GridLevel>();
        var spacingMultiplier = 1 + (_config.GridSpacingPercent / 100m);

        // Calculate buy levels (below center price)
        var buyPrice = centerPrice;
        for (var i = 0; i < _config.BuyLevels; i++)
        {
            buyPrice /= spacingMultiplier;
            var size = CalculateOrderSize(_config.OrderSizeUsdc, buyPrice);

            levels.Add(new GridLevel
            {
                Price = Math.Round(buyPrice, 2),
                Size = size,
                IsBuy = true,
                ClientOrderIndex = GetNextClientOrderIndex()
            });
        }

        // Calculate sell levels (above center price)
        var sellPrice = centerPrice;
        for (var i = 0; i < _config.SellLevels; i++)
        {
            sellPrice *= spacingMultiplier;
            var size = CalculateOrderSize(_config.OrderSizeUsdc, sellPrice);

            levels.Add(new GridLevel
            {
                Price = Math.Round(sellPrice, 2),
                Size = size,
                IsBuy = false,
                ClientOrderIndex = GetNextClientOrderIndex()
            });
        }

        return levels;
    }

    /// <inheritdoc />
    public decimal CalculateOrderSize(decimal usdcAmount, decimal price)
    {
        if (price <= 0)
            return 0;

        return Math.Round(usdcAmount / price, 8);
    }

    /// <inheritdoc />
    public long ToScaledPrice(decimal price)
    {
        // Lighter uses 2 decimal places for price (cents)
        return (long)(price * OrderConstants.PriceScale);
    }

    /// <inheritdoc />
    public long ToScaledAmount(decimal amount)
    {
        // Lighter uses 8 decimal places for base asset
        return (long)(amount * OrderConstants.BaseAssetScale);
    }

    private long GetNextClientOrderIndex()
    {
        return Interlocked.Increment(ref _nextClientOrderIndex);
    }
}
