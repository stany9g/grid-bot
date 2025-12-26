using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Funding rate information from Extended API.
/// </summary>
public sealed record FundingRateResponse
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Current funding rate (decimal, e.g., 0.0001 = 0.01%).
    /// Positive rate means longs pay shorts.
    /// </summary>
    [JsonPropertyName("fundingRate")]
    public required string FundingRate { get; init; }

    /// <summary>
    /// Predicted next funding rate.
    /// </summary>
    [JsonPropertyName("nextFundingRate")]
    public string? NextFundingRate { get; init; }

    /// <summary>
    /// Time until next funding event (epoch milliseconds).
    /// </summary>
    [JsonPropertyName("nextFundingTime")]
    public long? NextFundingTime { get; init; }

    /// <summary>
    /// Funding interval in hours.
    /// </summary>
    [JsonPropertyName("fundingIntervalHours")]
    public int? FundingIntervalHours { get; init; }
}
