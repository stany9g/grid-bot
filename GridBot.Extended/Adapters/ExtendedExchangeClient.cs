using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Aggregate exchange client for Extended DEX.
/// Implements <see cref="IExchangeClient"/>.
/// </summary>
internal sealed class ExtendedExchangeClient : IExchangeClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedExchangeClient"/> class.
    /// </summary>
    public ExtendedExchangeClient(
        string exchangeId,
        IExchangeConnection connection,
        IRealtimeDataProvider realtimeData,
        IOrderClient orders,
        IAccountClient account,
        IMarketDataClient marketData,
        IScalingProvider scaling,
        IAuthenticationProvider auth,
        bool isDryRun)
    {
        ExchangeId = exchangeId ?? throw new ArgumentNullException(nameof(exchangeId));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        RealtimeData = realtimeData ?? throw new ArgumentNullException(nameof(realtimeData));
        Orders = orders ?? throw new ArgumentNullException(nameof(orders));
        Account = account ?? throw new ArgumentNullException(nameof(account));
        MarketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        Scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));
        Auth = auth ?? throw new ArgumentNullException(nameof(auth));
        IsDryRunEnabled = isDryRun;
    }

    /// <inheritdoc />
    public string ExchangeId { get; }

    /// <inheritdoc />
    public ExchangeType ExchangeType => ExchangeType.Extended;

    /// <inheritdoc />
    public IExchangeConnection Connection { get; }

    /// <inheritdoc />
    public IRealtimeDataProvider RealtimeData { get; }

    /// <inheritdoc />
    public IOrderClient Orders { get; }

    /// <inheritdoc />
    public IAccountClient Account { get; }

    /// <inheritdoc />
    public IMarketDataClient MarketData { get; }

    /// <inheritdoc />
    public IScalingProvider Scaling { get; }

    /// <inheritdoc />
    public IAuthenticationProvider Auth { get; }

    /// <inheritdoc />
    public bool IsDryRunEnabled { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}
