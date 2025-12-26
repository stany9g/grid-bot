using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridBot.Extended.Models.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// HTTP client implementation for Extended DEX REST API.
/// Handles rate limiting, retries, and error handling.
/// </summary>
public sealed class ExtendedHttpClient : IExtendedHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ExtendedOptions _options;
    private readonly ILogger<ExtendedHttpClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client instance.</param>
    /// <param name="rateLimiter">Rate limiter.</param>
    /// <param name="options">Extended options.</param>
    /// <param name="logger">Logger instance.</param>
    public ExtendedHttpClient(
        HttpClient httpClient,
        RateLimiter rateLimiter,
        IOptions<ExtendedOptions> options,
        ILogger<ExtendedHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure base address and default headers
        _httpClient.BaseAddress = new Uri(_options.ApiUrl);
        _httpClient.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<IReadOnlyList<MarketInfo>>(
            HttpMethod.Get,
            "/info/markets",
            RequestPriority.Low,
            ct);
        return response ?? [];
    }

    /// <inheritdoc />
    public async Task<MarketStats> GetMarketStatsAsync(string market, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        return await SendAsync<MarketStats>(
            HttpMethod.Get,
            $"/info/markets/{Uri.EscapeDataString(market)}/stats",
            RequestPriority.Low,
            ct) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<OrderBookResponse> GetOrderBookAsync(string market, int depth = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        return await SendAsync<OrderBookResponse>(
            HttpMethod.Get,
            $"/info/markets/{Uri.EscapeDataString(market)}/orderbook?depth={depth}",
            RequestPriority.Low,
            ct) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CandleResponse>> GetCandlesAsync(
        string market,
        string candleType,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        ArgumentException.ThrowIfNullOrWhiteSpace(candleType);
        ArgumentException.ThrowIfNullOrWhiteSpace(interval);

        var response = await SendAsync<IReadOnlyList<CandleResponse>>(
            HttpMethod.Get,
            $"/info/candles/{Uri.EscapeDataString(market)}/{Uri.EscapeDataString(candleType)}?interval={Uri.EscapeDataString(interval)}&limit={limit}",
            RequestPriority.Low,
            ct);
        return response ?? [];
    }

    /// <inheritdoc />
    public async Task<FundingRateResponse> GetFundingRateAsync(string market, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        return await SendAsync<FundingRateResponse>(
            HttpMethod.Get,
            $"/info/{Uri.EscapeDataString(market)}/funding",
            RequestPriority.Low,
            ct) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<AccountInfoResponse> GetAccountInfoAsync(CancellationToken ct = default)
    {
        return await SendAsync<AccountInfoResponse>(
            HttpMethod.Get,
            "/user/account/info",
            RequestPriority.Medium,
            ct) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BalanceResponse>> GetBalancesAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<IReadOnlyList<BalanceResponse>>(
            HttpMethod.Get,
            "/user/balance",
            RequestPriority.Medium,
            ct);
        return response ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<IReadOnlyList<PositionResponse>>(
            HttpMethod.Get,
            "/user/positions",
            RequestPriority.Critical, // Critical for risk management
            ct);
        return response ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(string? market = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(market)
            ? "/user/orders"
            : $"/user/orders?market={Uri.EscapeDataString(market)}";

        var response = await SendAsync<IReadOnlyList<OrderResponse>>(
            HttpMethod.Get,
            path,
            RequestPriority.Medium,
            ct);
        return response ?? [];
    }

    /// <inheritdoc />
    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await SendAsync<CreateOrderResponse>(
            HttpMethod.Post,
            "/user/order",
            RequestPriority.High,
            ct,
            request) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/user/order/{Uri.EscapeDataString(orderId)}",
                RequestPriority.Critical,
                ct);
            return true;
        }
        catch (ExtendedApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Order not found - consider it already cancelled
            _logger.LogDebug("Order {OrderId} not found, treating as already cancelled", orderId);
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<MassCancelResponse> MassCancelOrdersAsync(MassCancelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await SendAsync<MassCancelResponse>(
            HttpMethod.Post,
            "/user/order/massCancel",
            RequestPriority.Critical,
            ct,
            request) ?? throw new ExtendedApiException("Empty response", HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task<int> GetLeverageAsync(string market, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        var response = await SendAsync<LeverageResponse>(
            HttpMethod.Get,
            $"/user/leverage?market={Uri.EscapeDataString(market)}",
            RequestPriority.Medium,
            ct);
        return response?.Leverage ?? 1;
    }

    /// <inheritdoc />
    public async Task<bool> SetLeverageAsync(string market, int leverage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);

        try
        {
            await SendAsync<object>(
                HttpMethod.Patch,
                "/user/leverage",
                RequestPriority.High,
                ct,
                new { market, leverage });
            return true;
        }
        catch (ExtendedApiException ex)
        {
            _logger.LogWarning(ex, "Failed to set leverage for {Market} to {Leverage}", market, leverage);
            return false;
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        RequestPriority priority,
        CancellationToken ct,
        object? body = null)
    {
        // Wait for rate limiter
        await _rateLimiter.WaitAsync(priority, ct);

        // CRITICAL: Record request BEFORE sending to account for in-flight requests
        _rateLimiter.RecordRequest();

        try
        {
            using var request = new HttpRequestMessage(method, path);

            if (body != null)
            {
                request.Content = JsonContent.Create(body, options: ExtendedJsonOptions.Default);
            }

            _logger.LogDebug("Sending {Method} {Path}", method, path);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _rateLimiter.RecordSuccessfulRequest();

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default;
                }

                return await response.Content.ReadFromJsonAsync<T>(ExtendedJsonOptions.Default, ct);
            }

            // Handle rate limiting
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.Seconds;
                _rateLimiter.RecordRateLimitHit((int?)retryAfter);
            }

            // Parse error response
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            ErrorResponse? errorResponse = null;

            try
            {
                errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, ExtendedJsonOptions.Default);
            }
            catch
            {
                // Ignore deserialization errors for error response
            }

            var errorMessage = errorResponse?.Error ?? errorContent ?? response.ReasonPhrase ?? "Unknown error";
            var errorCode = errorResponse?.ErrorCode;

            _logger.LogWarning(
                "API error: {StatusCode} {ErrorCode}: {Message}",
                response.StatusCode, errorCode, errorMessage);

            throw new ExtendedApiException(errorMessage, response.StatusCode, errorCode);
        }
        catch (ExtendedApiException)
        {
            throw;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request failed: {Method} {Path}", method, path);
            throw new ExtendedApiException(ex.Message, HttpStatusCode.InternalServerError, null, ex);
        }
    }

    private sealed record LeverageResponse
    {
        public int Leverage { get; init; }
    }
}
