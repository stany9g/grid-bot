using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Response from sending a single transaction via WebSocket.
/// Type: jsonapi/sendtx
/// </summary>
public sealed record SendTxWsResponse : WebSocketMessage
{
    /// <summary>
    /// Request identifier for correlation.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Response code. 200 or 0 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Transaction hash for the submitted transaction.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string TxHash { get; init; } = string.Empty;

    /// <summary>
    /// Predicted execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("predicted_execution_time_ms")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long PredictedExecutionTimeMs { get; init; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}

/// <summary>
/// Response from sending batch transactions via WebSocket.
/// Type: jsonapi/sendtxbatch
/// </summary>
public sealed record SendTxBatchWsResponse : WebSocketMessage
{
    /// <summary>
    /// Request identifier for correlation.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Response code. 200 or 0 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Array of transaction hashes.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string[] TxHashes { get; init; } = [];

    /// <summary>
    /// Predicted execution time in milliseconds (Unix timestamp).
    /// </summary>
    [JsonPropertyName("predicted_execution_time_ms")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long PredictedExecutionTimeMs { get; init; }

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;

    /// <summary>
    /// Gets the transaction hashes as an array (alias for TxHashes).
    /// </summary>
    [JsonIgnore]
    public string[] TxHashArray => TxHashes;
}
