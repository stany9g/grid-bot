using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Wrapper for Extended API responses that contain status and data.
/// </summary>
/// <typeparam name="T">The type of data in the response.</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>
    /// Status of the response: "OK" or "ERROR".
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// The actual response data (when status is OK).
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>
    /// Error details (when status is ERROR).
    /// </summary>
    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }

    /// <summary>
    /// Whether the response indicates success.
    /// </summary>
    public bool IsSuccess => Status.Equals("OK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Error details from Extended API.
/// </summary>
public sealed record ApiError
{
    /// <summary>
    /// Numeric error code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Error message (e.g., "NOT_ENOUGH_FUNDS", "INVALID_SIGNATURE").
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional debug information.
    /// </summary>
    [JsonPropertyName("debugInfo")]
    public string? DebugInfo { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        string.IsNullOrEmpty(DebugInfo)
            ? $"[{Code}] {Message}"
            : $"[{Code}] {Message}: {DebugInfo}";
}
