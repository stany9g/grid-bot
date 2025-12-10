namespace GridBot.Lighter;

/// <summary>
/// Exception thrown when Lighter API operations fail.
/// </summary>
public class LighterApiException : Exception
{
    /// <summary>
    /// Gets the HTTP status code associated with the error, if available.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets the error code from the API response, if available.
    /// </summary>
    public int? Code { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public LighterApiException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiException"/> class with a specified error message and status code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="statusCode">The HTTP status code or error code.</param>
    public LighterApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
        Code = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public LighterApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
