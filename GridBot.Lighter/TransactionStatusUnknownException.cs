namespace GridBot.Lighter;

/// <summary>
/// Exception thrown when a transaction's status cannot be determined (e.g., timeout).
/// The transaction may have succeeded, failed, or still be pending.
/// Callers should verify the transaction state before retrying.
/// </summary>
public sealed class TransactionStatusUnknownException : LighterApiException
{
    /// <summary>
    /// The request ID of the transaction with unknown status.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionStatusUnknownException"/> class.
    /// </summary>
    /// <param name="requestId">The request ID of the transaction.</param>
    /// <param name="message">The exception message.</param>
    public TransactionStatusUnknownException(string requestId, string message)
        : base(message)
    {
        RequestId = requestId;
    }
}
