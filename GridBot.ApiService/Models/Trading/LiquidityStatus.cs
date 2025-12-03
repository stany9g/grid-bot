namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Level of market liquidity.
/// </summary>
public enum LiquidityLevel
{
    /// <summary>
    /// Normal liquidity - all conditions met.
    /// </summary>
    Normal,

    /// <summary>
    /// Low liquidity - warning thresholds breached.
    /// Spreads should be widened.
    /// </summary>
    Low,

    /// <summary>
    /// Critical liquidity - critical thresholds breached.
    /// Trading should be paused or halted.
    /// </summary>
    Critical,

    /// <summary>
    /// Trading halted due to liquidity conditions.
    /// </summary>
    Halted
}

/// <summary>
/// Represents the current liquidity status for a market.
/// </summary>
public sealed class LiquidityStatus
{
    /// <summary>
    /// 24-hour trading volume in USD.
    /// </summary>
    public decimal Volume24h { get; init; }

    /// <summary>
    /// 7-day average trading volume in USD.
    /// </summary>
    public decimal Volume7dAvg { get; init; }

    /// <summary>
    /// Current volume as percentage of 7-day average.
    /// </summary>
    public decimal VolumeRatio { get; init; }

    /// <summary>
    /// Total order book depth (bid + ask) in USD.
    /// </summary>
    public decimal OrderBookDepth { get; init; }

    /// <summary>
    /// Bid side order book depth in USD.
    /// </summary>
    public decimal BidDepth { get; init; }

    /// <summary>
    /// Ask side order book depth in USD.
    /// </summary>
    public decimal AskDepth { get; init; }

    /// <summary>
    /// Current funding rate as percentage.
    /// </summary>
    public decimal FundingRate { get; init; }

    /// <summary>
    /// Current bid-ask spread as percentage.
    /// </summary>
    public decimal BidAskSpread { get; init; }

    /// <summary>
    /// Overall liquidity level.
    /// </summary>
    public LiquidityLevel Level { get; init; }

    /// <summary>
    /// Recommended spread multiplier based on liquidity conditions.
    /// 1.0 = normal, > 1.0 = widen spreads.
    /// </summary>
    public decimal RecommendedSpreadMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Whether trading is allowed given current liquidity.
    /// </summary>
    public bool TradingAllowed { get; init; } = true;

    /// <summary>
    /// Warning message if liquidity is not normal.
    /// </summary>
    public string Warning { get; init; } = string.Empty;

    /// <summary>
    /// Hours of consecutive low volume.
    /// </summary>
    public int ConsecutiveLowVolumeHours { get; init; }

    /// <summary>
    /// Whether volume is at warning level.
    /// </summary>
    public bool VolumeWarning { get; init; }

    /// <summary>
    /// Whether volume is at critical level.
    /// </summary>
    public bool VolumeCritical { get; init; }

    /// <summary>
    /// Whether order book depth is at warning level.
    /// </summary>
    public bool DepthWarning { get; init; }

    /// <summary>
    /// Whether order book depth is at critical level.
    /// </summary>
    public bool DepthCritical { get; init; }

    /// <summary>
    /// Whether funding rate is at warning level.
    /// </summary>
    public bool FundingWarning { get; init; }

    /// <summary>
    /// Whether funding rate is at critical level.
    /// </summary>
    public bool FundingCritical { get; init; }

    /// <summary>
    /// Creates a status indicating normal liquidity.
    /// </summary>
    public static LiquidityStatus Normal(decimal volume24h, decimal volume7dAvg, decimal depth, decimal fundingRate) => new()
    {
        Volume24h = volume24h,
        Volume7dAvg = volume7dAvg,
        VolumeRatio = volume7dAvg > 0 ? (volume24h / volume7dAvg) * 100m : 100m,
        OrderBookDepth = depth,
        FundingRate = fundingRate,
        Level = LiquidityLevel.Normal,
        RecommendedSpreadMultiplier = 1.0m,
        TradingAllowed = true,
        Warning = string.Empty
    };
}
