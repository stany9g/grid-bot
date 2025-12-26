using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Market statistics from Extended API.
/// </summary>
public sealed record MarketStats
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Current index price.
    /// </summary>
    [JsonPropertyName("indexPrice")]
    public required string IndexPrice { get; init; }

    /// <summary>
    /// Current mark price.
    /// </summary>
    [JsonPropertyName("markPrice")]
    public required string MarkPrice { get; init; }

    /// <summary>
    /// Best bid price.
    /// </summary>
    [JsonPropertyName("bestBid")]
    public string? BestBid { get; init; }

    /// <summary>
    /// Best ask price.
    /// </summary>
    [JsonPropertyName("bestAsk")]
    public string? BestAsk { get; init; }

    /// <summary>
    /// 24-hour trading volume in quote currency.
    /// </summary>
    [JsonPropertyName("volume24h")]
    public string? Volume24h { get; init; }

    /// <summary>
    /// 24-hour price change percentage.
    /// </summary>
    [JsonPropertyName("change24h")]
    public string? Change24h { get; init; }

    /// <summary>
    /// 24-hour high price.
    /// </summary>
    [JsonPropertyName("high24h")]
    public string? High24h { get; init; }

    /// <summary>
    /// 24-hour low price.
    /// </summary>
    [JsonPropertyName("low24h")]
    public string? Low24h { get; init; }

    /// <summary>
    /// Open interest in base asset.
    /// </summary>
    [JsonPropertyName("openInterest")]
    public string? OpenInterest { get; init; }
}
