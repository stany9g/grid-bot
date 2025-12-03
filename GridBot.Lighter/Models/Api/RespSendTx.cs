using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Response from sending a single transaction to the Lighter API.
/// </summary>
public sealed class RespSendTx
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
    /// Transaction hash for the submitted transaction.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string TxHash { get; set; } = string.Empty;

    /// <summary>
    /// Predicted execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("predicted_execution_time_ms")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long PredictedExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
