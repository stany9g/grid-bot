using GridBot.Abstractions.Factory;

namespace GridBot.ApiService.Services.Exchange;

/// <summary>
/// Information about an available exchange.
/// </summary>
/// <param name="Type">The exchange type.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="IsAvailable">Whether the exchange is configured and available.</param>
/// <param name="StatusMessage">Optional status message (e.g., connection status or error).</param>
public record ExchangeInfo(
    ExchangeType Type,
    string DisplayName,
    bool IsAvailable,
    string? StatusMessage);
