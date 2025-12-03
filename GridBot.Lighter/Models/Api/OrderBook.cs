using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents order book metadata for a market in the Lighter system.
/// </summary>
public sealed class OrderBook
{
    /// <summary>
    /// Market symbol (e.g., "BTC-USDC").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Market ID (unique identifier).
    /// </summary>
    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    /// <summary>
    /// Market status (e.g., "active", "inactive", "halted").
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Taker fee as a decimal string (e.g., "0.0005" for 0.05%).
    /// </summary>
    [JsonPropertyName("taker_fee")]
    public string TakerFee { get; set; } = "0";

    /// <summary>
    /// Maker fee as a decimal string (e.g., "0.0002" for 0.02%).
    /// </summary>
    [JsonPropertyName("maker_fee")]
    public string MakerFee { get; set; } = "0";

    /// <summary>
    /// Liquidation fee as a decimal string.
    /// </summary>
    [JsonPropertyName("liquidation_fee")]
    public string? LiquidationFee { get; set; }

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
    /// Maximum base amount for orders.
    /// </summary>
    [JsonPropertyName("max_base_amount")]
    public string? MaxBaseAmount { get; set; }

    /// <summary>
    /// Maximum quote amount for orders.
    /// </summary>
    [JsonPropertyName("max_quote_amount")]
    public string? MaxQuoteAmount { get; set; }

    /// <summary>
    /// Number of supported decimals for size (base amount).
    /// </summary>
    [JsonPropertyName("supported_size_decimals")]
    public int SupportedSizeDecimals { get; set; }

    /// <summary>
    /// Number of supported decimals for price.
    /// </summary>
    [JsonPropertyName("supported_price_decimals")]
    public int SupportedPriceDecimals { get; set; }

    /// <summary>
    /// Number of supported decimals for quote amount.
    /// </summary>
    [JsonPropertyName("supported_quote_decimals")]
    public int SupportedQuoteDecimals { get; set; }

    /// <summary>
    /// Base asset information.
    /// </summary>
    [JsonPropertyName("base_asset")]
    public string? BaseAsset { get; set; }

    /// <summary>
    /// Quote asset information.
    /// </summary>
    [JsonPropertyName("quote_asset")]
    public string? QuoteAsset { get; set; }

    /// <summary>
    /// Maximum leverage allowed for this market.
    /// </summary>
    [JsonPropertyName("max_leverage")]
    public string? MaxLeverage { get; set; }

    /// <summary>
    /// Market creation timestamp (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }
}

/// <summary>
/// Response wrapper for order books query.
/// </summary>
public sealed class OrderBooksResponse
{
    /// <summary>
    /// Response code. 200 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// List of order books.
    /// </summary>
    [JsonPropertyName("order_books")]
    public List<OrderBook> OrderBooks { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
