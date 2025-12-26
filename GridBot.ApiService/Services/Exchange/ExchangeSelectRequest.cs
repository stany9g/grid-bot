namespace GridBot.ApiService.Services.Exchange;

/// <summary>
/// Request model for selecting an exchange.
/// </summary>
/// <param name="ExchangeType">The exchange type to select (e.g., "Lighter", "Extended").</param>
public record ExchangeSelectRequest(string ExchangeType);
