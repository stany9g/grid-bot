using System.Text.Json;
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
    /// Transaction hashes for the submitted transactions.
    /// Can be comma-separated string or array.
    /// </summary>
    [JsonPropertyName("tx_hashes")]
    public JsonElement TxHashesRaw { get; set; }

    /// <summary>
    /// Predicted execution times in milliseconds for each transaction.
    /// Can be comma-separated string, number, or array.
    /// </summary>
    [JsonPropertyName("predicted_execution_time_ms")]
    public JsonElement PredictedExecutionTimeMsRaw { get; set; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;

    /// <summary>
    /// Gets the transaction hashes as an array.
    /// </summary>
    [JsonIgnore]
    public string[] TxHashArray
    {
        get
        {
            if (TxHashesRaw.ValueKind == JsonValueKind.String)
            {
                var str = TxHashesRaw.GetString() ?? "";
                return str.Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            if (TxHashesRaw.ValueKind == JsonValueKind.Array)
            {
                return TxHashesRaw.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToArray();
            }
            return [];
        }
    }

    /// <summary>
    /// Gets the predicted execution times as an array of integers.
    /// </summary>
    [JsonIgnore]
    public int[] PredictedExecutionTimeMsArray
    {
        get
        {
            if (PredictedExecutionTimeMsRaw.ValueKind == JsonValueKind.String)
            {
                var str = PredictedExecutionTimeMsRaw.GetString() ?? "";
                return str.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x, out var val) ? val : 0)
                    .ToArray();
            }
            if (PredictedExecutionTimeMsRaw.ValueKind == JsonValueKind.Number)
            {
                return [PredictedExecutionTimeMsRaw.GetInt32()];
            }
            if (PredictedExecutionTimeMsRaw.ValueKind == JsonValueKind.Array)
            {
                return PredictedExecutionTimeMsRaw.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0)
                    .ToArray();
            }
            return [];
        }
    }
}
