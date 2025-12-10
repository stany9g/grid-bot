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
    public async Task<AccountMetadata> GetAccountMetadataAsync(long accountIndex, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching account metadata via REST for account {AccountIndex}", accountIndex);

        var response = await _httpClient.GetAsync($"accountMetadata?by=index&value={accountIndex}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AccountMetadata>(LighterJsonOptions.Default, cancellationToken);
        return result ?? throw new LighterApiException("Failed to get account metadata - null response");
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
    public async Task<OrderBookDetail> GetOrderBookDetailsAsync(
        int marketId,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching order book details via REST for market {MarketId}", marketId);

        var queryParams = $"?market_id={marketId}";
        if (depth.HasValue)
            queryParams += $"&depth={depth.Value}";

        var response = await _httpClient.GetAsync($"orderBookDetails{queryParams}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OrderBookDetailResponse>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get order book details - null response",
                result?.Code ?? 0);
        }

        // Try to get data from either Data property (legacy) or OrderBookDetails array (actual API)
        if (result.Data != null)
            return result.Data;

        var detail = result.OrderBookDetails?.FirstOrDefault(d => d.MarketId == marketId);
        return detail ?? throw new LighterApiException($"Order book data for market {marketId} is null");
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
    public async Task<Tx> GetTransactionAsync(string hashOrIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hashOrIndex))
            throw new ArgumentException("Hash or index cannot be null or empty.", nameof(hashOrIndex));

        _logger.LogDebug("Fetching transaction via REST: {HashOrIndex}", hashOrIndex);

        var response = await _httpClient.GetAsync($"tx?hash_or_index={hashOrIndex}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<Tx>(LighterJsonOptions.Default, cancellationToken);
        return result ?? throw new LighterApiException("Failed to get transaction - null response");
    }

    /// <inheritdoc />
    public async Task<NextNonce> GetNextNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching next nonce via REST for account {AccountIndex}, apiKeyIndex {ApiKeyIndex}",
            accountIndex, apiKeyIndex);

        var queryParams = $"?account_index={accountIndex}&api_key_index={apiKeyIndex}";
        var response = await _httpClient.GetAsync($"nextNonce{queryParams}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NextNonce>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get next nonce - null response",
                result?.Code ?? 0);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        long startTimestamp,
        long endTimestamp,
        string resolution = "1h",
        int countBack = 20,
        bool setTimestampToEnd = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching candlesticks via REST for market {MarketId}, resolution {Resolution}",
            marketId, resolution);

        var queryParams = $"?market_id={marketId}&resolution={resolution}&start_timestamp={startTimestamp}&end_timestamp={endTimestamp}&count_back={countBack}";
        if (setTimestampToEnd)
            queryParams += "&set_timestamp_to_end=true";

        var response = await _httpClient.GetAsync($"candlesticks{queryParams}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CandlesticksResponse>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get candlesticks - null response",
                result?.Code ?? 0);
        }

        return result.Candlesticks;
    }

    /// <inheritdoc />
    public async Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        string resolution = "1h",
        int countBack = 20,
        CancellationToken cancellationToken = default)
    {
        var endTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Calculate start timestamp based on resolution
        var periodMs = resolution switch
        {
            "1m" => 60_000L,
            "5m" => 300_000L,
            "15m" => 900_000L,
            "1h" => 3_600_000L,
            "4h" => 14_400_000L,
            "1d" => 86_400_000L,
            _ => 3_600_000L
        };

        var startTimestamp = endTimestamp - (periodMs * countBack);

        return await GetCandlesticksAsync(marketId, startTimestamp, endTimestamp, resolution, countBack, false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<FundingRate>> GetFundingRatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching funding rates via REST");

        var response = await _httpClient.GetAsync("funding-rates", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FundingRatesResponse>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get funding rates - null response",
                result?.Code ?? 0);
        }

        return result.Data;
    }

    /// <inheritdoc />
    public async Task<List<Trade>> GetRecentTradesAsync(
        int marketId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching recent trades via REST for market {MarketId}", marketId);

        var queryParams = $"?market_id={marketId}&limit={limit}";
        var response = await _httpClient.GetAsync($"recentTrades{queryParams}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TradesResponse>(LighterJsonOptions.Default, cancellationToken);

        if (result == null || !result.IsSuccess)
        {
            throw new LighterApiException(
                result?.Message ?? "Failed to get recent trades - null response",
                result?.Code ?? 0);
        }

        return result.Data;
    }
}
