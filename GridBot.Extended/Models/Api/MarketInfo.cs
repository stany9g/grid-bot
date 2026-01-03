using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Market information from Extended API.
/// </summary>
public sealed record MarketInfo
{
    /// <summary>
    /// Market name/identifier (e.g., "BTC-USD").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// UI display name (e.g., "BTC-USD").
    /// </summary>
    [JsonPropertyName("uiName")]
    public string? UiName { get; init; }

    /// <summary>
    /// Market category (e.g., "L1", "Meme", "DeFi").
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// Base asset name (e.g., "BTC").
    /// </summary>
    [JsonPropertyName("assetName")]
    public required string AssetName { get; init; }

    /// <summary>
    /// Base asset decimal precision.
    /// </summary>
    [JsonPropertyName("assetPrecision")]
    public int AssetPrecision { get; init; }

    /// <summary>
    /// Collateral asset name (e.g., "USD").
    /// </summary>
    [JsonPropertyName("collateralAssetName")]
    public required string CollateralAssetName { get; init; }

    /// <summary>
    /// Collateral asset decimal precision.
    /// </summary>
    [JsonPropertyName("collateralAssetPrecision")]
    public int CollateralAssetPrecision { get; init; }

    /// <summary>
    /// Whether the market is active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    /// <summary>
    /// Market status (e.g., "ACTIVE").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Market statistics.
    /// </summary>
    [JsonPropertyName("marketStats")]
    public MarketStatsInfo? MarketStats { get; init; }

    /// <summary>
    /// Trading configuration.
    /// </summary>
    [JsonPropertyName("tradingConfig")]
    public TradingConfigInfo? TradingConfig { get; init; }

    /// <summary>
    /// L2 configuration for StarkEx.
    /// </summary>
    [JsonPropertyName("l2Config")]
    public L2ConfigInfo? L2Config { get; init; }

    /// <summary>
    /// Whether the market is visible on the UI.
    /// </summary>
    [JsonPropertyName("visibleOnUi")]
    public bool VisibleOnUi { get; init; }

    /// <summary>
    /// Market creation timestamp (milliseconds).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; init; }
}

/// <summary>
/// Market statistics information.
/// </summary>
public sealed record MarketStatsInfo
{
    [JsonPropertyName("dailyVolume")]
    public string? DailyVolume { get; init; }

    [JsonPropertyName("dailyVolumeBase")]
    public string? DailyVolumeBase { get; init; }

    [JsonPropertyName("dailyPriceChange")]
    public string? DailyPriceChange { get; init; }

    [JsonPropertyName("dailyPriceChangePercentage")]
    public string? DailyPriceChangePercentage { get; init; }

    [JsonPropertyName("dailyLow")]
    public string? DailyLow { get; init; }

    [JsonPropertyName("dailyHigh")]
    public string? DailyHigh { get; init; }

    [JsonPropertyName("lastPrice")]
    public string? LastPrice { get; init; }

    [JsonPropertyName("askPrice")]
    public string? AskPrice { get; init; }

    [JsonPropertyName("bidPrice")]
    public string? BidPrice { get; init; }

    [JsonPropertyName("markPrice")]
    public string? MarkPrice { get; init; }

    [JsonPropertyName("indexPrice")]
    public string? IndexPrice { get; init; }

    [JsonPropertyName("fundingRate")]
    public string? FundingRate { get; init; }

    [JsonPropertyName("nextFundingRate")]
    public long? NextFundingRate { get; init; }

    [JsonPropertyName("openInterest")]
    public string? OpenInterest { get; init; }

    [JsonPropertyName("openInterestBase")]
    public string? OpenInterestBase { get; init; }
}

/// <summary>
/// Trading configuration for a market.
/// </summary>
public sealed record TradingConfigInfo
{
    [JsonPropertyName("minOrderSize")]
    public string? MinOrderSize { get; init; }

    [JsonPropertyName("minOrderSizeChange")]
    public string? MinOrderSizeChange { get; init; }

    [JsonPropertyName("minPriceChange")]
    public string? MinPriceChange { get; init; }

    [JsonPropertyName("maxMarketOrderValue")]
    public string? MaxMarketOrderValue { get; init; }

    [JsonPropertyName("maxLimitOrderValue")]
    public string? MaxLimitOrderValue { get; init; }

    [JsonPropertyName("maxPositionValue")]
    public string? MaxPositionValue { get; init; }

    [JsonPropertyName("maxLeverage")]
    public string? MaxLeverage { get; init; }

    [JsonPropertyName("maxNumOrders")]
    public string? MaxNumOrders { get; init; }

    [JsonPropertyName("limitPriceCap")]
    public string? LimitPriceCap { get; init; }

    [JsonPropertyName("limitPriceFloor")]
    public string? LimitPriceFloor { get; init; }

    [JsonPropertyName("riskFactorConfig")]
    public IReadOnlyList<RiskFactorConfig>? RiskFactorConfig { get; init; }
}

/// <summary>
/// Risk factor configuration entry.
/// </summary>
public sealed record RiskFactorConfig
{
    [JsonPropertyName("upperBound")]
    public string? UpperBound { get; init; }

    [JsonPropertyName("riskFactor")]
    public string? RiskFactor { get; init; }

    [JsonPropertyName("isAvailableForUsers")]
    public bool IsAvailableForUsers { get; init; }
}

/// <summary>
/// L2 configuration for StarkEx.
/// </summary>
public sealed record L2ConfigInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("collateralId")]
    public string? CollateralId { get; init; }

    [JsonPropertyName("syntheticId")]
    public string? SyntheticId { get; init; }

    [JsonPropertyName("syntheticResolution")]
    public long SyntheticResolution { get; init; }

    [JsonPropertyName("collateralResolution")]
    public long CollateralResolution { get; init; }
}
