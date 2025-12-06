using System.Runtime.InteropServices;
using GridBot.Lighter.Models;
using GridBot.Lighter.Native;

namespace GridBot.Lighter;

/// <summary>
/// High-level client for signing Lighter protocol transactions.
/// Provides async/await API with result tuple error handling.
/// </summary>
public class SignerClient : IDisposable
{
    private long _currentNonce;
    private bool _isInitialized;
    private readonly object _nonceLock = new();

    /// <summary>
    /// Gets the current account index.
    /// </summary>
    public long AccountIndex { get; private set; }

    /// <summary>
    /// Gets the current API key index.
    /// </summary>
    public int ApiKeyIndex { get; private set; }

    /// <summary>
    /// Gets the chain ID (304 for mainnet, 300 for testnet).
    /// </summary>
    public int ChainId { get; private set; }

    /// <summary>
    /// Generates a new API key pair.
    /// </summary>
    /// <param name="seed">Optional seed string for deterministic key generation.</param>
    /// <returns>Tuple containing (privateKey, publicKey, error). If error is not null, key generation failed.</returns>
    public static async Task<(string? privateKey, string? publicKey, string? error)> GenerateApiKeyAsync(string? seed = null)
    {
        return await Task.Run(() =>
        {
            var response = NativeMethods.GenerateAPIKey(seed);
            return (
                MarshalString(response.PrivateKey),
                MarshalString(response.PublicKey),
                MarshalString(response.Err)
            );
        });
    }

    /// <summary>
    /// Initializes the signing client with API credentials and network settings.
    /// This must be called before any signing operations.
    /// </summary>
    /// <param name="url">API endpoint URL (e.g., "https://api.lighter.xyz").</param>
    /// <param name="privateKey">API private key for signing operations.</param>
    /// <param name="chainId">Blockchain network ID (use ChainId.Mainnet or ChainId.Testnet).</param>
    /// <param name="apiKeyIndex">Index of the API key to use (default 0).</param>
    /// <param name="accountIndex">Account identifier (default 0).</param>
    /// <param name="initialNonce">Starting nonce value (default 0).</param>
    /// <returns>Error message if initialization failed, null on success.</returns>
    public async Task<string?> InitializeAsync(
        string url,
        string privateKey,
        int chainId,
        int apiKeyIndex = 0,
        long accountIndex = 0,
        long initialNonce = 0)
    {
        if (_isInitialized)
            return "Client is already initialized";

        var error = await Task.Run(() =>
        {
            var errPtr = NativeMethods.CreateClient(url, privateKey, chainId, apiKeyIndex, accountIndex);
            return MarshalString(errPtr);
        });

        if (error == null)
        {
            _isInitialized = true;
            AccountIndex = accountIndex;
            ApiKeyIndex = apiKeyIndex;
            ChainId = chainId;
            _currentNonce = initialNonce;
        }

        return error;
    }

    /// <summary>
    /// Signs a request to create a single order.
    /// </summary>
    /// <param name="request">Order creation request with all parameters.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> CreateOrderAsync(CreateOrderRequest request)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        var validationError = request.Validate();
        if (validationError != null)
            return (null, validationError);

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();
            var result = NativeMethods.SignCreateOrder(
                request.MarketIndex,
                request.ClientOrderIndex,
                request.BaseAmount,
                request.Price,
                request.IsAsk ? 1 : 0,
                (int)request.OrderType,
                (int)request.TimeInForce,
                request.ReduceOnly ? 1 : 0,
                request.TriggerPrice,
                request.OrderExpiry,
                nonce
            );

            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Signs a request to create grouped orders (OCO, OTO, OTOCO).
    /// </summary>
    /// <param name="request">Grouped orders request.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> CreateGroupedOrdersAsync(CreateGroupedOrdersRequest request)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        var validationError = request.Validate();
        if (validationError != null)
            return (null, validationError);

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();

            // Convert high-level requests to native structs
            var nativeOrders = new CreateOrderTxReq[request.Orders.Count];
            for (int i = 0; i < request.Orders.Count; i++)
            {
                var order = request.Orders[i];
                nativeOrders[i] = new CreateOrderTxReq
                {
                    MarketIndex = (byte)order.MarketIndex,
                    ClientOrderIndex = order.ClientOrderIndex,
                    BaseAmount = order.BaseAmount,
                    Price = (uint)order.Price,
                    IsAsk = (byte)(order.IsAsk ? 1 : 0),
                    Type = (byte)order.OrderType,
                    TimeInForce = (byte)order.TimeInForce,
                    ReduceOnly = (byte)(order.ReduceOnly ? 1 : 0),
                    TriggerPrice = (uint)order.TriggerPrice,
                    OrderExpiry = order.OrderExpiry
                };
            }

            // Marshal array to unmanaged memory
            int structSize = Marshal.SizeOf<CreateOrderTxReq>();
            IntPtr ordersPtr = Marshal.AllocHGlobal(structSize * nativeOrders.Length);

            try
            {
                for (int i = 0; i < nativeOrders.Length; i++)
                {
                    IntPtr currentPtr = IntPtr.Add(ordersPtr, i * structSize);
                    Marshal.StructureToPtr(nativeOrders[i], currentPtr, false);
                }

                var result = NativeMethods.SignCreateGroupedOrders(
                    (byte)request.GroupingType,
                    ordersPtr,
                    nativeOrders.Length,
                    nonce
                );

                return ProcessStrOrErr(result);
            }
            finally
            {
                // Clean up unmanaged memory
                for (int i = 0; i < nativeOrders.Length; i++)
                {
                    IntPtr currentPtr = IntPtr.Add(ordersPtr, i * structSize);
                    Marshal.DestroyStructure<CreateOrderTxReq>(currentPtr);
                }
                Marshal.FreeHGlobal(ordersPtr);
            }
        });
    }

    /// <summary>
    /// Signs a request to cancel a specific order.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="orderId">ID of the order to cancel.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> CancelOrderAsync(int marketIndex, long orderId)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        if (marketIndex < 0)
            return (null, "MarketIndex must be non-negative");

        if (orderId <= 0)
            return (null, "OrderId must be positive");

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();
            var result = NativeMethods.SignCancelOrder(marketIndex, orderId, nonce);
            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Signs a request to cancel all orders in a specific market.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="cancelTimestampMs">Unix timestamp in milliseconds. Orders created before this timestamp will be cancelled.
    /// If 0 is passed, defaults to current time + 5 minutes. Must be greater than 0 when sent to the API.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> CancelAllOrdersAsync(int marketIndex, long cancelTimestampMs = 0)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        if (marketIndex < 0)
            return (null, "MarketIndex must be non-negative");

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();
            // If cancelTimestampMs is 0, use current time + 5 minutes as the cancel-all timestamp.
            // The native library expects this to be a Unix timestamp in milliseconds > 0.
            long effectiveTimestamp = cancelTimestampMs > 0
                ? cancelTimestampMs
                : DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

            var result = NativeMethods.SignCancelAllOrders(marketIndex, effectiveTimestamp, nonce);
            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Signs a request to modify an existing order.
    /// </summary>
    /// <param name="request">Order modification request.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> ModifyOrderAsync(ModifyOrderRequest request)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        var validationError = request.Validate();
        if (validationError != null)
            return (null, validationError);

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();
            var result = NativeMethods.SignModifyOrder(
                request.MarketIndex,
                request.OrderId,
                request.NewClientOrderIndex,
                request.NewBaseAmount,
                request.NewPrice,
                nonce
            );

            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Signs a request to update position leverage settings.
    /// </summary>
    /// <param name="request">Leverage update request.</param>
    /// <returns>Tuple containing (txInfo, error). If error is not null, signing failed.</returns>
    public async Task<(string? txInfo, string? error)> UpdateLeverageAsync(UpdateLeverageRequest request)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        var validationError = request.Validate();
        if (validationError != null)
            return (null, validationError);

        return await Task.Run(() =>
        {
            long nonce = GetNextNonce();
            var result = NativeMethods.SignUpdateLeverage(
                request.MarketIndex,
                (int)request.MarginMode,
                request.Leverage,
                nonce
            );

            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Creates an authentication token for accessing private API endpoints.
    /// The token is signed using the account's API private key.
    /// </summary>
    /// <param name="validitySeconds">How long the token should be valid (default 600 = 10 minutes).</param>
    /// <returns>Tuple containing (authToken, error). If error is not null, token creation failed.</returns>
    public async Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600)
    {
        if (!_isInitialized)
            return (null, "Client not initialized. Call InitializeAsync first.");

        return await Task.Run(() =>
        {
            var deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + validitySeconds;
            var result = NativeMethods.CreateAuthToken(deadline, ApiKeyIndex, AccountIndex);
            return ProcessStrOrErr(result);
        });
    }

    /// <summary>
    /// Gets the next nonce value in a thread-safe manner.
    /// </summary>
    private long GetNextNonce()
    {
        lock (_nonceLock)
        {
            var nextNonce = ++_currentNonce;
            Console.WriteLine($"[SignerClient] GetNextNonce: incremented to {nextNonce}, will use this nonce for signing");
            return nextNonce;
        }
    }

    /// <summary>
    /// Sets the current nonce value.
    /// Useful for synchronizing with the server nonce.
    /// </summary>
    /// <param name="nonce">The nonce value to set.</param>
    public void SetNonce(long nonce)
    {
        lock (_nonceLock)
        {
            Console.WriteLine($"[SignerClient] SetNonce: setting _currentNonce from {_currentNonce} to {nonce}");
            _currentNonce = nonce;
        }
    }

    /// <summary>
    /// Marshals an unmanaged string pointer to a managed string.
    /// </summary>
    private static string? MarshalString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return null;

        return Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// Processes a StrOrErr response and returns a result tuple.
    /// </summary>
    private static (string? result, string? error) ProcessStrOrErr(StrOrErr response)
    {
        return (MarshalString(response.Str), MarshalString(response.Err));
    }

    /// <summary>
    /// Disposes the client resources.
    /// </summary>
    public void Dispose()
    {
        _isInitialized = false;
        GC.SuppressFinalize(this);
    }
}
