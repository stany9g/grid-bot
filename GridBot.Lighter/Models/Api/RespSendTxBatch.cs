using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Response from sending a batch of transactions to the Lighter API.
/// </summary>
public sealed class RespSendTxBatch
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
    /// Transaction hashes for the submitted transactions, comma-separated.
    /// </summary>
    [JsonPropertyName("tx_hashes")]
    public string TxHashes { get; set; } = string.Empty;

    /// <summary>
    /// Predicted execution times in milliseconds for each transaction, comma-separated.
    /// </summary>
    [JsonPropertyName("predicted_execution_time_ms")]
    public string PredictedExecutionTimeMs { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;

    /// <summary>
    /// Gets the transaction hashes as an array.
    /// </summary>
    [JsonIgnore]
    public string[] TxHashArray => TxHashes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Gets the predicted execution times as an array of integers.
    /// </summary>
    [JsonIgnore]
    public int[] PredictedExecutionTimeMsArray =>
        PredictedExecutionTimeMs.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var val) ? val : 0)
            .ToArray();
}
