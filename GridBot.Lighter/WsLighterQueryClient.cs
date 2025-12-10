using System.Globalization;
using System.Net.Http.Json;
using GridBot.Lighter.Models.Api;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// WebSocket-based implementation of ILighterQueryClient.
/// Reads real-time data from WebSocket cache.
/// Uses REST for operations not available via WebSocket (market list, candlesticks).
/// </summary>
public sealed class WsLighterQueryClient : ILighterQueryClient
{
    private readonly ILighterRealtimeState _state;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WsLighterQueryClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsLighterQueryClient"/> class.
    /// </summary>
    /// <param name="state">Real-time state service providing WebSocket-cached data.</param>
    /// <param name="httpClient">HTTP client for REST operations (market list, candlesticks).</param>
    /// <param name="logger">Logger instance.</param>
    public WsLighterQueryClient(
        ILighterRealtimeState state,
        HttpClient httpClient,
        ILogger<WsLighterQueryClient> logger)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<Account> GetAccountAsync(long accountIndex, CancellationToken cancellationToken = default)
    {
        var snapshot = _state.GetAccount();
        if (snapshot == null)
        {
            _logger.LogWarning("No account data available from WebSocket");
            throw new LighterApiException("Account data not available - WebSocket not connected or no data received yet");
        }

        if (snapshot.AccountId != accountIndex)
        {
            _logger.LogWarning("Requested account {Requested} but WebSocket is subscribed to {Subscribed}",
                accountIndex, snapshot.AccountId);
        }

        // Convert WebSocket snapshot to API model
        var account = new Account
        {
            AccountIndex = snapshot.AccountId,
            Index = snapshot.AccountId,
            Collateral = snapshot.Collateral.ToString(CultureInfo.InvariantCulture),
            AvailableBalance = snapshot.AvailableBalance.ToString(CultureInfo.InvariantCulture),
            TotalAssetValue = snapshot.PortfolioValue.ToString(CultureInfo.InvariantCulture),
            Status = 1, // Active
            Positions = snapshot.Positions.Values.Select(p => new Position
            {
                MarketId = p.MarketId,
                Positionn = Math.Abs(p.Size).ToString(CultureInfo.InvariantCulture),
                Sign = p.Size >= 0 ? (p.Size > 0 ? 1 : 0) : -1,
                AvgEntryPrice = p.AvgEntryPrice.ToString(CultureInfo.InvariantCulture),
                UnrealizedPnl = p.UnrealizedPnl.ToString(CultureInfo.InvariantCulture),
                LiquidationPrice = p.LiquidationPrice.ToString(CultureInfo.InvariantCulture),
                MarginMode = p.IsCross ? 0 : 1
            }).ToList()
        };

        return Task.FromResult(account);
    }

    /// <inheritdoc />
    public Task<AccountMetadata> GetAccountMetadataAsync(long accountIndex, CancellationToken cancellationToken = default)
    {
        // AccountMetadata is not available via WebSocket
        throw new NotSupportedException(
            "GetAccountMetadataAsync is not supported in WebSocket-only mode. " +
            "AccountMetadata requires REST API access.");
    }

    /// <inheritdoc />
    public Task<List<Order>> GetActiveOrdersAsync(
        long accountIndex,
        int marketId,
        string authToken,
        CancellationToken cancellationToken = default)
    {
        var orderSnapshots = _state.GetOrders(marketId);

        var orders = orderSnapshots
            .Where(o => o.Status == "open" || o.Status == "partial")
            .Select(o => new Order
            {
                OrderIndex = o.OrderIndex,
                AccountIndex = accountIndex,
                MarketId = marketId,
                Price = o.Price.ToString(CultureInfo.InvariantCulture),
                InitialBaseAmount = o.Size.ToString(CultureInfo.InvariantCulture),
                FilledBaseAmount = o.FilledSize.ToString(CultureInfo.InvariantCulture),
                RemainingBaseAmount = (o.Size - o.FilledSize).ToString(CultureInfo.InvariantCulture),
                Side = o.IsBuy ? "buy" : "sell",
                Status = o.Status,
                Type = "limit" // WebSocket doesn't provide order type details
            })
            .ToList();

        return Task.FromResult(orders);
    }

    /// <inheritdoc />
    public async Task<List<OrderBook>> GetOrderBooksAsync(CancellationToken cancellationToken = default)
    {
        // Market list is not available via WebSocket - use REST
        _logger.LogDebug("Fetching order books via REST (not available via WebSocket)");

        var response = await _httpClient.GetAsync("orderBooks", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OrderBooksResponse>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get order books - null response",
                result?.Code ?? 0);
        }

        return result.OrderBooks;
    }

    /// <inheritdoc />
    public Task<OrderBookDetail> GetOrderBookDetailsAsync(
        int marketId,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        // OrderBookDetail contains market metadata (fees, margins, etc.) which is not available via WebSocket.
        // For actual bids/asks, use GetOrderBookOrdersAsync instead.
        throw new NotSupportedException(
            "GetOrderBookDetailsAsync is not supported in WebSocket-only mode. " +
            "Market metadata requires REST API access. " +
            "Use GetOrderBookOrdersAsync for bid/ask data.");
    }

    /// <inheritdoc />
    public Task<OrderBookOrdersResponse> GetOrderBookOrdersAsync(
        int marketId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _state.GetOrderBook(marketId);
        if (snapshot == null)
        {
            _logger.LogWarning("No order book data available from WebSocket for market {MarketId}", marketId);
            throw new LighterApiException($"Order book data not available for market {marketId} - not subscribed or no data received yet");
        }

        var bids = snapshot.Bids
            .Take(limit ?? int.MaxValue)
            .Select(b => new OrderBookOrder
            {
                Price = b.Price.ToString(CultureInfo.InvariantCulture),
                RemainingBaseAmount = b.Size.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        var asks = snapshot.Asks
            .Take(limit ?? int.MaxValue)
            .Select(a => new OrderBookOrder
            {
                Price = a.Price.ToString(CultureInfo.InvariantCulture),
                RemainingBaseAmount = a.Size.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        var response = new OrderBookOrdersResponse
        {
            Code = 200,
            Bids = bids,
            Asks = asks,
            TotalBids = snapshot.Bids.Count,
            TotalAsks = snapshot.Asks.Count
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<Tx> GetTransactionAsync(string hashOrIndex, CancellationToken cancellationToken = default)
    {
        // Transaction lookup is not available via WebSocket
        throw new NotSupportedException(
            "GetTransactionAsync is not supported in WebSocket-only mode. " +
            "Transaction lookup requires REST API access.");
    }

    /// <inheritdoc />
    public Task<NextNonce> GetNextNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        // Nonce is not available via WebSocket
        throw new NotSupportedException(
            "GetNextNonceAsync is not supported in WebSocket-only mode. " +
            "Nonce synchronization requires REST API access. " +
            "Use SignerClient's local nonce tracking instead.");
    }

    /// <inheritdoc />
    public Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        long startTimestamp,
        long endTimestamp,
        string resolution = "1h",
        int countBack = 20,
        bool setTimestampToEnd = false,
        CancellationToken cancellationToken = default)
    {
        // Historical candlestick data is not available via WebSocket
        throw new NotSupportedException(
            "GetCandlesticksAsync is not supported in WebSocket-only mode. " +
            "Historical candlestick data requires REST API access.");
    }

    /// <inheritdoc />
    public Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        string resolution = "1h",
        int countBack = 20,
        CancellationToken cancellationToken = default)
    {
        // Historical candlestick data is not available via WebSocket
        throw new NotSupportedException(
            "GetCandlesticksAsync is not supported in WebSocket-only mode. " +
            "Historical candlestick data requires REST API access.");
    }

    /// <inheritdoc />
    public Task<List<FundingRate>> GetFundingRatesAsync(CancellationToken cancellationToken = default)
    {
        // Full funding rates list is not available via WebSocket
        // We could return current funding rate from subscribed markets but not the full list
        throw new NotSupportedException(
            "GetFundingRatesAsync is not supported in WebSocket-only mode. " +
            "Full funding rates list requires REST API access. " +
            "Use GetMarketStats for current funding rate of subscribed markets.");
    }

    /// <inheritdoc />
    public Task<List<Trade>> GetRecentTradesAsync(
        int marketId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        // Recent trades are not available via WebSocket
        throw new NotSupportedException(
            "GetRecentTradesAsync is not supported in WebSocket-only mode. " +
            "Recent trades require REST API access.");
    }
}
