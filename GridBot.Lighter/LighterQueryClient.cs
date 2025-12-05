using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// HTTP client for read-only Lighter REST API operations.
/// Provides methods to query account, market, and transaction data.
/// </summary>
public sealed class LighterQueryClient : ILighterQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<LighterQueryClient>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterQueryClient"/> class with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    /// <param name="logger">Optional logger for debugging API responses.</param>
    public LighterQueryClient(HttpClient httpClient, ILogger<LighterQueryClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jsonOptions = CreateJsonOptions();
        _logger = logger;
    }

    /// <summary>
    /// Gets the next nonce for an account from the server.
    /// Useful for nonce recovery and synchronization.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing the next nonce value.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<NextNonce> GetNextNonceAsync(
        long accountIndex,
        int apiKeyIndex,
        CancellationToken cancellationToken = default)
    {
        var queryParams = $"?account_index={accountIndex}&api_key_index={apiKeyIndex}";
        var response = await GetAsync<NextNonce>($"nextNonce{queryParams}", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get next nonce", response.Code);

        return response;
    }

    /// <summary>
    /// Gets a transaction by its hash or sequence index.
    /// </summary>
    /// <param name="hashOrIndex">Transaction hash (0x...) or sequence index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction details.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<Tx> GetTransactionAsync(
        string hashOrIndex,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hashOrIndex))
            throw new ArgumentException("Hash or index cannot be null or empty.", nameof(hashOrIndex));

        return await GetAsync<Tx>($"tx?hash_or_index={hashOrIndex}", cancellationToken);
    }

    /// <summary>
    /// Gets account information by account index.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account details including positions and balances.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<Account> GetAccountAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<AccountResponse>($"account?by=index&value={accountIndex}", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException($"Failed to get account: code {response.Code}", response.Code);

        if (response.Accounts.Count == 0)
            throw new LighterApiException($"Account with index {accountIndex} not found");

        return response.Accounts[0];
    }

    /// <summary>
    /// Gets account metadata by account index.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account metadata including public key and status.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<AccountMetadata> GetAccountMetadataAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
    {
        return await GetAsync<AccountMetadata>($"accountMetadata?by=index&value={accountIndex}", cancellationToken);
    }

    /// <summary>
    /// Gets active orders for an account.
    /// This endpoint requires authentication via SDK-generated signatures.
    /// </summary>
    /// <param name="accountIndex">Account index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active orders for the account.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<List<Order>> GetActiveOrdersAsync(
        long accountIndex,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<ActiveOrdersResponse>($"accountActiveOrders?account_index={accountIndex}", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get active orders", response.Code);

        return response.Orders;
    }

    /// <summary>
    /// Gets all order book metadata for all markets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of order books with market information, fees, and trading limits.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<List<OrderBook>> GetOrderBooksAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<OrderBooksResponse>("orderBooks", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get order books", response.Code);

        return response.OrderBooks;
    }

    /// <summary>
    /// Gets detailed order book data for a specific market, including bids and asks.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="depth">Maximum number of price levels to return (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book details with bid/ask levels.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<OrderBookDetail> GetOrderBookDetailsAsync(
        int marketId,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = $"?market_id={marketId}";
        if (depth.HasValue)
            queryParams += $"&depth={depth.Value}";

        var response = await GetAsync<OrderBookDetailResponse>($"orderBookDetails{queryParams}", cancellationToken);

        _logger?.LogDebug(
            "OrderBookDetails response - Code: {Code}, IsSuccess: {IsSuccess}, Message: {Message}, HasData: {HasData}, HasOrderBookDetails: {HasOrderBookDetails}",
            response.Code, response.IsSuccess, response.Message, response.Data != null, response.OrderBookDetails?.Count ?? 0);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get order book details", response.Code);

        // Try to get data from either Data property (legacy) or OrderBookDetails array (actual API)
        if (response.Data != null)
            return response.Data;

        var detail = response.OrderBookDetails?.FirstOrDefault(d => d.MarketId == marketId);
        return detail ?? throw new LighterApiException($"Order book data for market {marketId} is null");
    }

    /// <summary>
    /// Gets order book orders (bids and asks) for a specific market.
    /// This returns the actual order book depth with individual orders.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="limit">Maximum number of orders per side to return (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book orders with bids and asks.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<OrderBookOrdersResponse> GetOrderBookOrdersAsync(
        int marketId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = $"?market_id={marketId}";
        if (limit.HasValue)
            queryParams += $"&limit={limit.Value}";

        var response = await GetAsync<OrderBookOrdersResponse>($"orderBookOrders{queryParams}", cancellationToken);

        _logger?.LogDebug(
            "OrderBookOrders response - Code: {Code}, IsSuccess: {IsSuccess}, TotalBids: {TotalBids}, TotalAsks: {TotalAsks}",
            response.Code, response.IsSuccess, response.TotalBids, response.TotalAsks);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get order book orders", response.Code);

        return response;
    }

    /// <summary>
    /// Gets candlestick (OHLCV) data for a market with explicit timestamp range.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="startTimestamp">Start timestamp in milliseconds (Unix epoch).</param>
    /// <param name="endTimestamp">End timestamp in milliseconds (Unix epoch).</param>
    /// <param name="resolution">Candle resolution (1m, 5m, 15m, 1h, 4h, 1d). Default is 1h.</param>
    /// <param name="countBack">Number of candles to return. Default is 20.</param>
    /// <param name="setTimestampToEnd">If true, sets timestamp to end of candle period. Default is false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of candlesticks ordered by timestamp ascending.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        long startTimestamp,
        long endTimestamp,
        string resolution = "1h",
        int countBack = 20,
        bool setTimestampToEnd = false,
        CancellationToken cancellationToken = default)
    {
        var queryParams = $"?market_id={marketId}&resolution={resolution}&start_timestamp={startTimestamp}&end_timestamp={endTimestamp}&count_back={countBack}";
        if (setTimestampToEnd)
            queryParams += "&set_timestamp_to_end=true";

        var response = await GetAsync<CandlesticksResponse>($"candlesticks{queryParams}", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get candlesticks", response.Code);

        return response.Candlesticks;
    }

    /// <summary>
    /// Gets candlestick (OHLCV) data for a market. Automatically calculates timestamps based on resolution and countBack.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="resolution">Candle resolution (1m, 5m, 15m, 1h, 4h, 1d). Default is 1h.</param>
    /// <param name="countBack">Number of candles to return. Default is 20.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of candlesticks ordered by timestamp ascending.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
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

    /// <summary>
    /// Gets current funding rates across exchanges for all markets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of funding rates.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<List<FundingRate>> GetFundingRatesAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<FundingRatesResponse>("funding-rates", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get funding rates", response.Code);

        return response.Data;
    }

    /// <summary>
    /// Gets recent trades for a market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="limit">Maximum number of trades to return. Default is 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent trades ordered by timestamp descending.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<List<Trade>> GetRecentTradesAsync(
        int marketId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var queryParams = $"?market_id={marketId}&limit={limit}";
        var response = await GetAsync<TradesResponse>($"recentTrades{queryParams}", cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get recent trades", response.Code);

        return response.Data;
    }

    private async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogInformation("API Response for {Endpoint}: {RawJson}", endpoint, rawJson);
            await EnsureSuccessStatusCodeAsync(response);
    

            var result = JsonSerializer.Deserialize<T>(rawJson, _jsonOptions);
            return result ?? throw new LighterApiException("Failed to deserialize response");
        }
        catch (HttpRequestException ex)
        {
            throw new LighterApiException($"HTTP request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LighterApiException("Request timed out", ex);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON deserialization failed for {Endpoint}", endpoint);
            throw new LighterApiException($"Failed to deserialize response: {ex.Message}", ex);
        }
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new LighterApiException(
                $"API request failed with status {(int)response.StatusCode}: {content}",
                (int)response.StatusCode);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }
}
