using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Calculates dynamic grid parameters based on ATR volatility.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class GridCalculator : IGridCalculator
{
    private readonly IRiskConfiguration _config;
    private readonly ILogger<GridCalculator> _logger;

    /// <summary>
    /// Tolerance for biasing grid levels toward liquidity clusters.
    /// If a level is within this percentage of a cluster, it will be adjusted.
    /// </summary>
    private const decimal ClusterBiasTolerance = 0.003m; // 0.3%

    public GridCalculator(IRiskConfiguration config, ILogger<GridCalculator> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public GridParameters CalculateGridParameters(decimal currentPrice, decimal atr, decimal atrPercent)
    {
        if (currentPrice <= 0)
        {
            throw new ArgumentException("Current price must be positive", nameof(currentPrice));
        }

        // Get spacing and orders per side based on ATR
        var (spacing, ordersPerSide) = _config.CalculateGridParameters(atrPercent);

        // Apply floor and ceiling from configuration
        spacing = Math.Max(spacing, _config.Grid.MinSpacing);
        spacing = Math.Min(spacing, _config.Grid.MaxSpacing);
        ordersPerSide = Math.Max(ordersPerSide, _config.Grid.MinOrdersPerSide);
        ordersPerSide = Math.Min(ordersPerSide, _config.Grid.MaxOrdersPerSide);

        // Calculate total grid width
        var totalWidth = spacing * ordersPerSide * 2; // Both sides

        // Apply width constraints
        totalWidth = Math.Max(totalWidth, _config.Grid.MinWidth);
        totalWidth = Math.Min(totalWidth, _config.Grid.MaxWidth);

        // Recalculate spacing if width was constrained
        var effectiveSpacing = totalWidth / (ordersPerSide * 2);
        effectiveSpacing = Math.Max(effectiveSpacing, _config.Grid.MinSpacing);

        // Calculate bounds
        var halfWidth = totalWidth / 200m; // Convert percentage to decimal (e.g., 5% -> 0.025)
        var upperBound = currentPrice * (1 + halfWidth);
        var lowerBound = currentPrice * (1 - halfWidth);

        _logger.LogDebug(
            "Calculated grid parameters: ATR={AtrPercent:F2}%, Spacing={Spacing:F2}%, Orders/Side={OrdersPerSide}, Width={Width:F2}%",
            atrPercent, effectiveSpacing, ordersPerSide, totalWidth);

        return new GridParameters
        {
            CenterPrice = currentPrice,
            GridSpacing = effectiveSpacing,
            OrdersPerSide = ordersPerSide,
            UpperBound = upperBound,
            LowerBound = lowerBound,
            TotalWidth = totalWidth
        };
    }

    /// <inheritdoc />
    public List<GridLevel> CalculateGridLevels(
        decimal currentPrice,
        GridParameters parameters,
        List<LiquidityCluster>? clusters = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var levels = new List<GridLevel>(parameters.OrdersPerSide * 2);
        var spacingMultiplier = parameters.GridSpacing / 100m;

        // Calculate default order size (to be overridden by order manager with proper sizing)
        // This is a placeholder - actual sizing is done by GridOrderManager
        const decimal defaultSize = 0.001m;

        // Generate bid levels (below current price)
        for (var i = 1; i <= parameters.OrdersPerSide; i++)
        {
            var rawPrice = currentPrice * (1 - spacingMultiplier * i);
            var adjustedPrice = BiasTowardCluster(rawPrice, clusters, isBid: true);

            // Ensure we don't go below lower bound
            if (adjustedPrice < parameters.LowerBound)
            {
                adjustedPrice = parameters.LowerBound;
            }

            levels.Add(new GridLevel
            {
                Price = adjustedPrice,
                IsBid = true,
                LevelIndex = i,
                Size = defaultSize,
                Status = GridLevelStatus.Pending
            });
        }

        // Generate ask levels (above current price)
        for (var i = 1; i <= parameters.OrdersPerSide; i++)
        {
            var rawPrice = currentPrice * (1 + spacingMultiplier * i);
            var adjustedPrice = BiasTowardCluster(rawPrice, clusters, isBid: false);

            // Ensure we don't go above upper bound
            if (adjustedPrice > parameters.UpperBound)
            {
                adjustedPrice = parameters.UpperBound;
            }

            levels.Add(new GridLevel
            {
                Price = adjustedPrice,
                IsBid = false,
                LevelIndex = i,
                Size = defaultSize,
                Status = GridLevelStatus.Pending
            });
        }

        _logger.LogDebug(
            "Calculated {Count} grid levels: {BidCount} bids, {AskCount} asks",
            levels.Count,
            levels.Count(l => l.IsBid),
            levels.Count(l => !l.IsBid));

        return levels;
    }

    /// <inheritdoc />
    public decimal CalculateGridSpacingFromAtr(decimal atrPercent)
    {
        // ATR-based spacing rules per risk specification:
        // ATR < 0.5%: spacing = 0.2%, orders = 10
        // 0.5% <= ATR < 1.0%: spacing = 0.5%, orders = 8
        // 1.0% <= ATR < 2.0%: spacing = 1.0%, orders = 6
        // 2.0% <= ATR < 3.0%: spacing = 1.5%, orders = 5
        // ATR >= 3.0%: spacing = 2.0%, orders = 4

        var spacing = atrPercent switch
        {
            < 0.5m => 0.2m,
            < 1.0m => 0.5m,
            < 2.0m => 1.0m,
            < 3.0m => 1.5m,
            _ => 2.0m
        };

        // Apply constraints
        spacing = Math.Max(spacing, _config.Grid.MinSpacing);
        spacing = Math.Min(spacing, _config.Grid.MaxSpacing);

        return spacing;
    }

    /// <summary>
    /// Attempts to bias a grid level price toward a nearby liquidity cluster.
    /// </summary>
    /// <param name="rawPrice">The original calculated price.</param>
    /// <param name="clusters">Available liquidity clusters.</param>
    /// <param name="isBid">Whether this is a bid level.</param>
    /// <returns>The possibly adjusted price.</returns>
    private decimal BiasTowardCluster(decimal rawPrice, List<LiquidityCluster>? clusters, bool isBid)
    {
        if (clusters == null || clusters.Count == 0)
        {
            return rawPrice;
        }

        // Find the nearest cluster on the correct side
        var relevantClusters = clusters.Where(c => c.IsBid == isBid).ToList();

        foreach (var cluster in relevantClusters)
        {
            var distance = Math.Abs(cluster.Price - rawPrice) / rawPrice;

            if (distance <= ClusterBiasTolerance)
            {
                _logger.LogTrace(
                    "Biasing {Side} level from {RawPrice} to cluster at {ClusterPrice} (distance: {Distance:P2})",
                    isBid ? "bid" : "ask", rawPrice, cluster.Price, distance);

                return cluster.Price;
            }
        }

        return rawPrice;
    }
}
