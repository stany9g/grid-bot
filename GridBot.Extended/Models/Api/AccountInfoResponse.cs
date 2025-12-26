using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Account information from Extended API.
/// </summary>
public sealed record AccountInfoResponse
{
    /// <summary>
    /// Account address.
    /// </summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>
    /// Stark public key.
    /// </summary>
    [JsonPropertyName("starkKey")]
    public string? StarkKey { get; init; }

    /// <summary>
    /// Current nonce for signing.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; init; }

    /// <summary>
    /// Account status: "active", "suspended", etc.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Whether the account is a market maker.
    /// </summary>
    [JsonPropertyName("isMarketMaker")]
    public bool IsMarketMaker { get; init; }

    /// <summary>
    /// Account tier for fee discounts.
    /// </summary>
    [JsonPropertyName("tier")]
    public string? Tier { get; init; }

    /// <summary>
    /// Maker fee rate.
    /// </summary>
    [JsonPropertyName("makerFee")]
    public string? MakerFee { get; init; }

    /// <summary>
    /// Taker fee rate.
    /// </summary>
    [JsonPropertyName("takerFee")]
    public string? TakerFee { get; init; }
}
