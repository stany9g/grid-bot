using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents detailed order book data for a specific market.
/// Note: For actual bids/asks, use the GetOrderBookOrdersAsync method.
/// This endpoint returns market metadata and trading stats.
/// </summary>
public sealed class OrderBookDetail
{
    /// <summary>
    /// Market ID.
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Market symbol (e.g., "BTC").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Market status (e.g., "active").
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Taker fee as decimal string.
    /// </summary>
    [JsonPropertyName("taker_fee")]
    public string TakerFee { get; set; } = "0";

    /// <summary>
    /// Maker fee as decimal string.
    /// </summary>
    [JsonPropertyName("maker_fee")]
    public string MakerFee { get; set; } = "0";

    /// <summary>
    /// Liquidation fee as decimal string.
    /// </summary>
    [JsonPropertyName("liquidation_fee")]
    public string LiquidationFee { get; set; } = "0";

    /// <summary>
    /// Minimum base amount for orders.
    /// </summary>
    [JsonPropertyName("min_base_amount")]
    public string MinBaseAmount { get; set; } = "0";

    /// <summary>
    /// Minimum quote amount for orders.
    /// </summary>
    [JsonPropertyName("min_quote_amount")]
    public string MinQuoteAmount { get; set; } = "0";

    /// <summary>
    /// Supported size decimals.
    /// </summary>
    [JsonPropertyName("supported_size_decimals")]
    public int SupportedSizeDecimals { get; set; }

    /// <summary>
    /// Supported price decimals.
    /// </summary>
    [JsonPropertyName("supported_price_decimals")]
    public int SupportedPriceDecimals { get; set; }

    /// <summary>
    /// Supported quote decimals.
    /// </summary>
    [JsonPropertyName("supported_quote_decimals")]
    public int SupportedQuoteDecimals { get; set; }

    /// <summary>
    /// Size decimals used for orders.
    /// </summary>
    [JsonPropertyName("size_decimals")]
    public int SizeDecimals { get; set; }

    /// <summary>
    /// Price decimals used for orders.
    /// </summary>
    [JsonPropertyName("price_decimals")]
    public int PriceDecimals { get; set; }

    /// <summary>
    /// Quote multiplier.
    /// </summary>
    [JsonPropertyName("quote_multiplier")]
    public int QuoteMultiplier { get; set; }

    /// <summary>
    /// Default initial margin fraction (in basis points, e.g., 500 = 5%).
    /// </summary>
    [JsonPropertyName("default_initial_margin_fraction")]
    public int DefaultInitialMarginFraction { get; set; }

    /// <summary>
    /// Minimum initial margin fraction (in basis points).
    /// </summary>
    [JsonPropertyName("min_initial_margin_fraction")]
    public int MinInitialMarginFraction { get; set; }

    /// <summary>
    /// Maintenance margin fraction (in basis points).
    /// </summary>
    [JsonPropertyName("maintenance_margin_fraction")]
    public int MaintenanceMarginFraction { get; set; }

    /// <summary>
    /// Closeout margin fraction (in basis points).
    /// </summary>
    [JsonPropertyName("closeout_margin_fraction")]
    public int CloseoutMarginFraction { get; set; }

    /// <summary>
    /// Last trade price (numeric value from API).
    /// </summary>
    [JsonPropertyName("last_trade_price")]
    public decimal LastTradePrice { get; set; }

    /// <summary>
    /// 24-hour price change percentage.
    /// </summary>
    [JsonPropertyName("daily_price_change")]
    public decimal DailyPriceChange { get; set; }

    /// <summary>
    /// 24-hour high price.
    /// </summary>
    [JsonPropertyName("daily_price_high")]
    public decimal DailyPriceHigh { get; set; }

    /// <summary>
    /// 24-hour low price.
    /// </summary>
    [JsonPropertyName("daily_price_low")]
    public decimal DailyPriceLow { get; set; }

    /// <summary>
    /// Open interest in base token units.
    /// </summary>
    [JsonPropertyName("open_interest")]
    public decimal OpenInterest { get; set; }

    /// <summary>
    /// 24-hour trading volume in base token.
    /// </summary>
    [JsonPropertyName("daily_base_token_volume")]
    public decimal DailyBaseTokenVolume { get; set; }

    /// <summary>
    /// 24-hour trading volume in quote token.
    /// </summary>
    [JsonPropertyName("daily_quote_token_volume")]
    public decimal DailyQuoteTokenVolume { get; set; }

    /// <summary>
    /// Number of trades in the last 24 hours.
    /// </summary>
    [JsonPropertyName("daily_trades_count")]
    public long DailyTradesCount { get; set; }
}

/// <summary>
/// Represents a price level in the order book (bid or ask).
/// </summary>
public sealed class OrderBookLevel
{
    /// <summary>
    /// Price level.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    /// <summary>
    /// Total size (quantity) at this price level.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    /// <summary>
    /// Number of orders at this price level.
    /// </summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }
}

/// <summary>
/// Response wrapper for order book details query.
/// </summary>
public sealed class OrderBookDetailResponse
{
    /// <summary>
    /// Response code. 200 indicates success (HTTP status code convention).
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Order book details (legacy format - may be null).
    /// </summary>
    [JsonPropertyName("data")]
    public OrderBookDetail? Data { get; set; }

    /// <summary>
    /// Order book details array from the actual API response.
    /// The API returns data in this property, not "data".
    /// </summary>
    [JsonPropertyName("order_book_details")]
    public List<OrderBookDetail>? OrderBookDetails { get; set; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
