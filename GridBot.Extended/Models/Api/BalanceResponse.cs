using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Balance information from Extended API.
/// </summary>
public sealed record BalanceResponse
{
    /// <summary>
    /// Asset symbol (e.g., "USDC").
    /// </summary>
    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    /// <summary>
    /// Total balance.
    /// </summary>
    [JsonPropertyName("total")]
    public required string Total { get; init; }

    /// <summary>
    /// Available balance (not locked in orders or positions).
    /// </summary>
    [JsonPropertyName("available")]
    public required string Available { get; init; }

    /// <summary>
    /// Locked balance (in open orders).
    /// </summary>
    [JsonPropertyName("locked")]
    public string? Locked { get; init; }

    /// <summary>
    /// Balance in positions (margin).
    /// </summary>
    [JsonPropertyName("inPositions")]
    public string? InPositions { get; init; }
}
