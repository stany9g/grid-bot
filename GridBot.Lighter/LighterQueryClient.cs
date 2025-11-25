using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter;

/// <summary>
/// HTTP client for read-only Lighter REST API operations.
/// Provides methods to query account, market, and transaction data.
/// </summary>
public sealed class LighterQueryClient : ILighterQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterQueryClient"/> class with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    public LighterQueryClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jsonOptions = CreateJsonOptions();
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
        return await GetAsync<Account>($"account?by=index&value={accountIndex}", cancellationToken);
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

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Failed to get order book details", response.Code);

        return response.Data ?? throw new LighterApiException("Order book data is null");
    }

    private async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            await EnsureSuccessStatusCodeAsync(response);

            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new LighterApiException("Failed to deserialize response");
        }
        catch (HttpRequestException ex)
        {
            throw new LighterApiException($"HTTP request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LighterApiException("Request timed out", ex);
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
