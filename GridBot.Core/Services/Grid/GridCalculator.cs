using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Configuration;

namespace GridBot.Core.Services.Grid;

/// <summary>
/// Calculates grid levels using runtime configuration.
/// Uses EffectiveValue from ConfigValue for auto-tunable parameters.
/// Scaling is now handled by the exchange adapters - this class works with decimal values.
/// </summary>
public sealed class GridCalculator : IGridCalculator
{
    private readonly IGridConfigurationService _configService;
    private long _nextClientOrderIndex;

    public GridCalculator(IGridConfigurationService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _nextClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <inheritdoc />
    public List<GridLevel> CalculateLevels(decimal centerPrice)
    {
        var config = _configService.Current;
        var levels = new List<GridLevel>();
        var spacingMultiplier = 1 + (config.GridSpacingPercent.EffectiveValue / 100m);
        var orderSize = config.OrderSizeUsdc.EffectiveValue;
        var buyLevels = config.BuyLevels.EffectiveValue;
        var sellLevels = config.SellLevels.EffectiveValue;

        // Calculate buy levels (below center price)
        var buyPrice = centerPrice;
        for (var i = 0; i < buyLevels; i++)
        {
            buyPrice /= spacingMultiplier;
            var size = CalculateOrderSize(orderSize, buyPrice);

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
        for (var i = 0; i < sellLevels; i++)
        {
            sellPrice *= spacingMultiplier;
            var size = CalculateOrderSize(orderSize, sellPrice);

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

    private long GetNextClientOrderIndex()
    {
        return Interlocked.Increment(ref _nextClientOrderIndex);
    }
}
