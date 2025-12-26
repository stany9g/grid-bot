using System.Net;

namespace GridBot.Extended;

/// <summary>
/// Exception thrown when an Extended API call fails.
/// </summary>
public sealed class ExtendedApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="errorCode">The application-level error code (if available).</param>
    /// <param name="innerException">The inner exception (if any).</param>
    public ExtendedApiException(
        string message,
        HttpStatusCode statusCode,
        string? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the HTTP status code from the API response.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the application-level error code (e.g., "NONCE_INVALID", "INSUFFICIENT_MARGIN").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets whether this error should be retried.
    /// </summary>
    public bool ShouldRetry => StatusCode switch
    {
        HttpStatusCode.TooManyRequests => true,  // 429 - apply rate limit backoff
        HttpStatusCode.InternalServerError => true,  // 500
        HttpStatusCode.BadGateway => true,  // 502
        HttpStatusCode.ServiceUnavailable => true,  // 503
        HttpStatusCode.GatewayTimeout => true,  // 504
        HttpStatusCode.Unauthorized when ErrorCode?.StartsWith("NONCE", StringComparison.OrdinalIgnoreCase) == true => true,
        _ => false
    };

    /// <summary>
    /// Gets whether this is a nonce-related error that requires nonce synchronization.
    /// </summary>
    public bool IsNonceError =>
        ErrorCode?.StartsWith("NONCE", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets whether this is a rate limit error (HTTP 429).
    /// </summary>
    public bool IsRateLimitError => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Gets whether this is an authentication error.
    /// </summary>
    public bool IsAuthError => StatusCode == HttpStatusCode.Unauthorized || StatusCode == HttpStatusCode.Forbidden;

    /// <summary>
    /// Gets the maximum number of retry attempts for this type of error.
    /// </summary>
    public int MaxRetryAttempts => StatusCode switch
    {
        HttpStatusCode.Unauthorized when IsNonceError => 5,  // Nonce errors get more retries
        HttpStatusCode.TooManyRequests => 3,  // Rate limit
        HttpStatusCode.InternalServerError => 3,
        HttpStatusCode.BadGateway => 3,
        HttpStatusCode.ServiceUnavailable => 3,
        HttpStatusCode.GatewayTimeout => 3,
        _ => 0
    };
}

/// <summary>
/// Exception thrown when order confirmation status is unknown (timeout).
/// Do NOT automatically retry - verify order state first.
/// </summary>
public sealed class OrderStatusUnknownException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderStatusUnknownException"/> class.
    /// </summary>
    /// <param name="orderId">The order ID that timed out.</param>
    /// <param name="message">The error message.</param>
    public OrderStatusUnknownException(string orderId, string message)
        : base(message)
    {
        OrderId = orderId;
    }

    /// <summary>
    /// Gets the order ID for which status is unknown.
    /// </summary>
    public string OrderId { get; }
}

/// <summary>
/// Exception thrown when Stark signature generation fails.
/// </summary>
public sealed class StarkSignatureException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StarkSignatureException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception (if any).</param>
    public StarkSignatureException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
