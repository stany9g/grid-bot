using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Account information from Extended API.
/// </summary>
public sealed record AccountInfoResponse
{
    /// <summary>
    /// Unique account identifier.
    /// </summary>
    [JsonPropertyName("accountId")]
    public long AccountId { get; init; }

    /// <summary>
    /// Account description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Account index.
    /// </summary>
    [JsonPropertyName("accountIndex")]
    public int AccountIndex { get; init; }

    /// <summary>
    /// Account status: "ACTIVE", "SUSPENDED", etc.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// L2 key for the account.
    /// </summary>
    [JsonPropertyName("l2Key")]
    public string? L2Key { get; init; }

    /// <summary>
    /// L2 vault identifier.
    /// </summary>
    [JsonPropertyName("l2Vault")]
    public string? L2Vault { get; init; }

    /// <summary>
    /// Bridge Starknet address.
    /// </summary>
    [JsonPropertyName("bridgeStarknetAddress")]
    public string? BridgeStarknetAddress { get; init; }

    /// <summary>
    /// API keys associated with the account.
    /// </summary>
    [JsonPropertyName("apiKeys")]
    public IReadOnlyList<string>? ApiKeys { get; init; }

    /// <summary>
    /// Account index used for key generation.
    /// </summary>
    [JsonPropertyName("accountIndexForKeyGeneration")]
    public int AccountIndexForKeyGeneration { get; init; }
}
