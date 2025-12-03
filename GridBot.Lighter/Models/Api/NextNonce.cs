using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Response from querying the next nonce for an account.
/// </summary>
public sealed class NextNonce
{
    /// <summary>
    /// Response code. 200 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message. "Success" for successful operations.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The next nonce value for the account.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; set; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
