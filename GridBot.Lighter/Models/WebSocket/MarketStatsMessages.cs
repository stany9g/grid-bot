using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Market stats update message.
/// Channel: market_stats/{MARKET_INDEX}
/// </summary>
public sealed record MarketStatsMessage : WebSocketMessage
{
    /// <summary>
    /// Market statistics data.
    /// </summary>
    [JsonPropertyName("market_stats")]
    public MarketStatsData? MarketStats { get; init; }
}

/// <summary>
/// Market statistics data structure.
/// </summary>
public sealed record MarketStatsData
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; init; }

    /// <summary>
    /// Index price.
    /// </summary>
    [JsonPropertyName("index_price")]
    public string IndexPrice { get; init; } = "0";

    /// <summary>
    /// Mark price.
    /// </summary>
    [JsonPropertyName("mark_price")]
    public string MarkPrice { get; init; } = "0";

    /// <summary>
    /// Open interest.
    /// </summary>
    [JsonPropertyName("open_interest")]
    public string OpenInterest { get; init; } = "0";

    /// <summary>
    /// Last trade price.
    /// </summary>
    [JsonPropertyName("last_trade_price")]
    public string LastTradePrice { get; init; } = "0";

    /// <summary>
    /// Current funding rate.
    /// </summary>
    [JsonPropertyName("current_funding_rate")]
    public string CurrentFundingRate { get; init; } = "0";

    /// <summary>
    /// Funding rate.
    /// </summary>
    [JsonPropertyName("funding_rate")]
    public string FundingRate { get; init; } = "0";

    /// <summary>
    /// Funding timestamp.
    /// </summary>
    [JsonPropertyName("funding_timestamp")]
    public long FundingTimestamp { get; init; }

    /// <summary>
    /// Daily base token volume.
    /// </summary>
    [JsonPropertyName("daily_base_token_volume")]
    public decimal DailyBaseTokenVolume { get; init; }

    /// <summary>
    /// Daily quote token volume.
    /// </summary>
    [JsonPropertyName("daily_quote_token_volume")]
    public decimal DailyQuoteTokenVolume { get; init; }

    /// <summary>
    /// Daily price low.
    /// </summary>
    [JsonPropertyName("daily_price_low")]
    public decimal DailyPriceLow { get; init; }

    /// <summary>
    /// Daily price high.
    /// </summary>
    [JsonPropertyName("daily_price_high")]
    public decimal DailyPriceHigh { get; init; }

    /// <summary>
    /// Daily price change percentage.
    /// </summary>
    [JsonPropertyName("daily_price_change")]
    public decimal DailyPriceChange { get; init; }
}

/// <summary>
/// User stats message.
/// Channel: user_stats/{ACCOUNT_ID}
/// </summary>
public sealed record UserStatsMessage : WebSocketMessage
{
    /// <summary>
    /// User statistics data.
    /// </summary>
    [JsonPropertyName("stats")]
    public UserStatsData? Stats { get; init; }
}

/// <summary>
/// User statistics data structure.
/// </summary>
public sealed record UserStatsData
{
    /// <summary>
    /// Collateral amount.
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    /// <summary>
    /// Portfolio value.
    /// </summary>
    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    /// <summary>
    /// Leverage level.
    /// </summary>
    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    /// <summary>
    /// Margin usage percentage.
    /// </summary>
    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    /// <summary>
    /// Buying power.
    /// </summary>
    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";

    /// <summary>
    /// Cross margin statistics.
    /// </summary>
    [JsonPropertyName("cross_stats")]
    public UserStatsDetail? CrossStats { get; init; }

    /// <summary>
    /// Total statistics.
    /// </summary>
    [JsonPropertyName("total_stats")]
    public UserStatsDetail? TotalStats { get; init; }
}

/// <summary>
/// Detailed user statistics for cross/total.
/// </summary>
public sealed record UserStatsDetail
{
    /// <summary>
    /// Collateral amount.
    /// </summary>
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    /// <summary>
    /// Portfolio value.
    /// </summary>
    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    /// <summary>
    /// Leverage level.
    /// </summary>
    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    /// <summary>
    /// Available balance.
    /// </summary>
    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    /// <summary>
    /// Margin usage percentage.
    /// </summary>
    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    /// <summary>
    /// Buying power.
    /// </summary>
    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";
}
