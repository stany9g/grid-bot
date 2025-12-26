using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Market information from Extended API.
/// </summary>
public sealed record MarketInfo
{
    /// <summary>
    /// Market identifier (e.g., "BTC-USD-PERP").
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Base asset symbol (e.g., "BTC").
    /// </summary>
    [JsonPropertyName("baseAsset")]
    public required string BaseAsset { get; init; }

    /// <summary>
    /// Quote asset symbol (e.g., "USD").
    /// </summary>
    [JsonPropertyName("quoteAsset")]
    public required string QuoteAsset { get; init; }

    /// <summary>
    /// Minimum order size in base asset.
    /// </summary>
    [JsonPropertyName("minOrderSize")]
    public required string MinOrderSize { get; init; }

    /// <summary>
    /// Maximum order size in base asset.
    /// </summary>
    [JsonPropertyName("maxOrderSize")]
    public string? MaxOrderSize { get; init; }

    /// <summary>
    /// Minimum price tick size.
    /// </summary>
    [JsonPropertyName("tickSize")]
    public required string TickSize { get; init; }

    /// <summary>
    /// Minimum quantity step size.
    /// </summary>
    [JsonPropertyName("stepSize")]
    public required string StepSize { get; init; }

    /// <summary>
    /// Maximum leverage allowed.
    /// </summary>
    [JsonPropertyName("maxLeverage")]
    public required int MaxLeverage { get; init; }

    /// <summary>
    /// Initial margin requirement (decimal, e.g., 0.1 = 10%).
    /// </summary>
    [JsonPropertyName("initialMarginFraction")]
    public string? InitialMarginFraction { get; init; }

    /// <summary>
    /// Maintenance margin requirement (decimal).
    /// </summary>
    [JsonPropertyName("maintenanceMarginFraction")]
    public string? MaintenanceMarginFraction { get; init; }

    /// <summary>
    /// Whether the market is currently active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>
    /// Price decimal places for display.
    /// </summary>
    [JsonPropertyName("priceDecimals")]
    public int PriceDecimals { get; init; }

    /// <summary>
    /// Size decimal places for display.
    /// </summary>
    [JsonPropertyName("sizeDecimals")]
    public int SizeDecimals { get; init; }
}
