using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents metadata for an account in the Lighter system.
/// </summary>
public sealed class AccountMetadata
{
    /// <summary>
    /// Account index.
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; set; }

    /// <summary>
    /// Layer 1 address associated with this account.
    /// </summary>
    [JsonPropertyName("l1_address")]
    public string L1Address { get; set; } = string.Empty;

    /// <summary>
    /// Public key for the account.
    /// </summary>
    [JsonPropertyName("public_key")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Account creation timestamp (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp (Unix timestamp in seconds).
    /// </summary>
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; set; }

    /// <summary>
    /// Account status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Account type.
    /// </summary>
    [JsonPropertyName("account_type")]
    public string AccountType { get; set; } = string.Empty;
}
