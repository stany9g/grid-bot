using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridBot.Lighter.Models;
using GridBot.Lighter.Models.Api;

namespace GridBot.Lighter.Api;

/// <summary>
/// HTTP client for the Lighter REST API.
/// Provides methods to submit signed transactions and query account/order/market data.
/// </summary>
public sealed class LighterApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Default base URL for the Lighter mainnet API.
    /// </summary>
    public const string DefaultVersion = "/api/v1";



    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiClient"/> class with a custom base URL.
    /// </summary>
    /// <param name="baseUrl">The base URL for the Lighter API.</param>
    public LighterApiClient(string baseUrl) : this(CreateHttpClient($"{baseUrl}{DefaultVersion}/"))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiClient"/> class with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    public LighterApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jsonOptions = CreateJsonOptions();
    }

    /// <summary>
    /// Submits a single signed transaction to the Lighter API.
    /// </summary>
    /// <param name="txType">Transaction type (see <see cref="TransactionTypes"/>).</param>
    /// <param name="txInfo">Signed transaction info JSON string from SignerClient.</param>
    /// <param name="priceProtection">Enable price protection (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hash and predicted execution time.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<RespSendTx> SendTransactionAsync(
        int txType,
        string txInfo,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txInfo))
            throw new ArgumentException("Transaction info cannot be null or empty.", nameof(txInfo));

        var request = new
        {
            tx_type = txType,
            tx_info = txInfo,
            price_protection = priceProtection
        };

        var response = await PostAsync<RespSendTx>("sendTx", request, cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Transaction submission failed", response.Code);

        return response;
    }

    /// <summary>
    /// Submits multiple signed transactions in a batch to the Lighter API.
    /// </summary>
    /// <param name="txTypes">Array of transaction types.</param>
    /// <param name="txInfos">Array of signed transaction info JSON strings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing transaction hashes and predicted execution times.</returns>
    /// <exception cref="LighterApiException">Thrown when the API returns an error.</exception>
    public async Task<RespSendTxBatch> SendTransactionBatchAsync(
        int[] txTypes,
        string[] txInfos,
        CancellationToken cancellationToken = default)
    {
        if (txTypes == null || txTypes.Length == 0)
            throw new ArgumentException("Transaction types cannot be null or empty.", nameof(txTypes));

        if (txInfos == null || txInfos.Length == 0)
            throw new ArgumentException("Transaction infos cannot be null or empty.", nameof(txInfos));

        if (txTypes.Length != txInfos.Length)
            throw new ArgumentException("Transaction types and infos arrays must have the same length.");

        var request = new
        {
            tx_types = string.Join(",", txTypes),
            tx_infos = string.Join(",", txInfos)
        };

        var response = await PostAsync<RespSendTxBatch>("sendTxBatch", request, cancellationToken);

        if (!response.IsSuccess)
            throw new LighterApiException(response.Message ?? "Batch transaction submission failed", response.Code);

        return response;
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
        return await GetAsync<AccountMetadata>($"accountMetadata?account_index={accountIndex}", cancellationToken);
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

    #region HTTP Helper Methods

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

    private async Task<T> PostAsync<T>(string endpoint, object request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
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

    #endregion

    #region Factory Methods

    private static HttpClient CreateHttpClient(string baseUrl)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        //client.DefaultRequestHeaders.Add("User-Agent", "GridBot.Lighter/1.0");
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        return client;
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

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _httpClient?.Dispose();
        _disposed = true;
    }

    #endregion
}

/// <summary>
/// Exception thrown when the Lighter API returns an error.
/// </summary>
public class LighterApiException : Exception
{
    /// <summary>
    /// HTTP status code (if applicable).
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// API error code (if applicable).
    /// </summary>
    public int? ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="statusCodeOrErrorCode">HTTP status code or API error code.</param>
    public LighterApiException(string message, int? statusCodeOrErrorCode = null) : base(message)
    {
        if (statusCodeOrErrorCode >= 100 && statusCodeOrErrorCode < 600)
            StatusCode = statusCodeOrErrorCode;
        else
            ErrorCode = statusCodeOrErrorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterApiException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public LighterApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
