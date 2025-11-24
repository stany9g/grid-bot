using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents a transaction in the Lighter system.
/// </summary>
public sealed class Tx
{
    /// <summary>
    /// Transaction hash.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Transaction type (1-64).
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>
    /// Transaction info as JSON string.
    /// </summary>
    [JsonPropertyName("info")]
    public string Info { get; set; } = string.Empty;

    /// <summary>
    /// Event info as JSON string (for executed transactions).
    /// </summary>
    [JsonPropertyName("event_info")]
    public string? EventInfo { get; set; }

    /// <summary>
    /// Transaction status (e.g., "queued", "executed", "failed").
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Transaction index within the sequence.
    /// </summary>
    [JsonPropertyName("transaction_index")]
    public int TransactionIndex { get; set; }

    /// <summary>
    /// Layer 1 address that submitted the transaction.
    /// </summary>
    [JsonPropertyName("l1_address")]
    public string L1Address { get; set; } = string.Empty;

    /// <summary>
    /// Account index for the transaction.
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; set; }

    /// <summary>
    /// Nonce used for this transaction.
    /// </summary>
    [JsonPropertyName("nonce")]
    public long Nonce { get; set; }

    /// <summary>
    /// Expiration timestamp for the transaction.
    /// </summary>
    [JsonPropertyName("expire_at")]
    public long ExpireAt { get; set; }

    /// <summary>
    /// Block height when the transaction was executed (if executed).
    /// </summary>
    [JsonPropertyName("block_height")]
    public long? BlockHeight { get; set; }

    /// <summary>
    /// Timestamp when the transaction was queued.
    /// </summary>
    [JsonPropertyName("queued_at")]
    public long QueuedAt { get; set; }

    /// <summary>
    /// Timestamp when the transaction was executed (if executed).
    /// </summary>
    [JsonPropertyName("executed_at")]
    public long? ExecutedAt { get; set; }

    /// <summary>
    /// Sequence index for the transaction.
    /// </summary>
    [JsonPropertyName("sequence_index")]
    public long SequenceIndex { get; set; }

    /// <summary>
    /// Parent transaction hash (for grouped transactions).
    /// </summary>
    [JsonPropertyName("parent_hash")]
    public string? ParentHash { get; set; }
}
