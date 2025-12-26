using GridBot.Abstractions.Factory;

namespace GridBot.ApiService.Services.Exchange;

/// <summary>
/// Service for managing exchange selection and switching.
/// </summary>
public interface IExchangeSelectionService
{
    /// <summary>
    /// Gets the currently selected exchange type.
    /// </summary>
    ExchangeType? CurrentExchangeType { get; }

    /// <summary>
    /// Gets the currently selected exchange client.
    /// </summary>
    IExchangeClient? CurrentClient { get; }

    /// <summary>
    /// Gets the list of available exchanges with their status.
    /// </summary>
    IReadOnlyList<ExchangeInfo> AvailableExchanges { get; }

    /// <summary>
    /// Event raised when the selected exchange changes.
    /// </summary>
    event EventHandler<ExchangeType>? ExchangeChanged;

    /// <summary>
    /// Selects an exchange to use for trading.
    /// </summary>
    /// <param name="exchangeType">The exchange type to select.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is running.</exception>
    /// <exception cref="ArgumentException">Thrown when exchange is not available.</exception>
    Task SelectExchangeAsync(ExchangeType exchangeType, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the list of available exchanges.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshAvailableExchangesAsync(CancellationToken ct = default);
}
