using GridBot.Abstractions.Authentication;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Aggregate exchange client for Lighter DEX implementing <see cref="IExchangeClient"/>.
/// Provides unified access to all Lighter trading capabilities through abstraction interfaces.
/// </summary>
internal sealed class LighterExchangeClient : IExchangeClient
{
    private readonly LighterOptions _options;
    private readonly ILogger<LighterExchangeClient> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterExchangeClient"/> class.
    /// </summary>
    /// <param name="exchangeId">The unique identifier for this exchange instance.</param>
    /// <param name="connection">The connection adapter.</param>
    /// <param name="realtimeData">The realtime data adapter.</param>
    /// <param name="orders">The order client adapter.</param>
    /// <param name="account">The account client adapter.</param>
    /// <param name="marketData">The market data client adapter.</param>
    /// <param name="scaling">The scaling provider adapter.</param>
    /// <param name="auth">The authentication provider adapter.</param>
    /// <param name="options">Lighter configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterExchangeClient(
        string exchangeId,
        IExchangeConnection connection,
        IRealtimeDataProvider realtimeData,
        IOrderClient orders,
        IAccountClient account,
        IMarketDataClient marketData,
        IScalingProvider scaling,
        IAuthenticationProvider auth,
        IOptions<LighterOptions> options,
        ILogger<LighterExchangeClient> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeId);

        ExchangeId = exchangeId;
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        RealtimeData = realtimeData ?? throw new ArgumentNullException(nameof(realtimeData));
        Orders = orders ?? throw new ArgumentNullException(nameof(orders));
        Account = account ?? throw new ArgumentNullException(nameof(account));
        MarketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        Scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));
        Auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ExchangeId { get; }

    /// <inheritdoc />
    public ExchangeType ExchangeType => ExchangeType.Lighter;

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
    public bool IsDryRunEnabled => _options.DryRun;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("Disposing LighterExchangeClient {ExchangeId}", ExchangeId);

        await Connection.DisposeAsync();

        _logger.LogDebug("LighterExchangeClient {ExchangeId} disposed", ExchangeId);
    }
}
