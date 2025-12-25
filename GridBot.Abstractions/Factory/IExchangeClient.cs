using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;

namespace GridBot.Abstractions.Factory;

/// <summary>
/// Aggregate interface providing access to all exchange capabilities.
/// Represents a complete exchange client with all sub-components.
/// </summary>
public interface IExchangeClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this exchange instance.
    /// </summary>
    string ExchangeId { get; }

    /// <summary>
    /// Gets the type of exchange.
    /// </summary>
    ExchangeType ExchangeType { get; }

    /// <summary>
    /// Gets the connection manager for this exchange.
    /// </summary>
    IExchangeConnection Connection { get; }

    /// <summary>
    /// Gets the real-time data provider for this exchange.
    /// </summary>
    IRealtimeDataProvider RealtimeData { get; }

    /// <summary>
    /// Gets the order client for this exchange.
    /// </summary>
    IOrderClient Orders { get; }

    /// <summary>
    /// Gets the account client for this exchange.
    /// </summary>
    IAccountClient Account { get; }

    /// <summary>
    /// Gets the market data client for this exchange.
    /// </summary>
    IMarketDataClient MarketData { get; }

    /// <summary>
    /// Gets the scaling provider for this exchange.
    /// </summary>
    IScalingProvider Scaling { get; }

    /// <summary>
    /// Gets the authentication provider for this exchange.
    /// </summary>
    IAuthenticationProvider Auth { get; }

    /// <summary>
    /// Gets whether dry-run mode is enabled (no real orders placed).
    /// </summary>
    bool IsDryRunEnabled { get; }
}
