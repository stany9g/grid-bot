using System.Runtime.InteropServices;

namespace GridBot.Lighter.Native;

/// <summary>
/// P/Invoke declarations for the Lighter native signing library.
/// Handles platform-specific library loading and marshaling between C# and native code.
/// </summary>
internal static partial class NativeMethods
{
    private const string LibraryName = "signer-amd64";

    /// <summary>
    /// Static constructor to configure native library loading based on platform.
    /// </summary>
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    /// <summary>
    /// Resolves the correct native library path based on the current platform.
    /// </summary>
    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        string libPath = GetNativeLibraryPath();

        if (!File.Exists(libPath))
        {
            throw new FileNotFoundException($"Native library not found at: {libPath}");
        }

        return NativeLibrary.Load(libPath);
    }

    /// <summary>
    /// Gets the platform-specific native library path.
    /// </summary>
    private static string GetNativeLibraryPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string nativeDir = Path.Combine(baseDirectory, "Native");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(nativeDir, "signer-amd64.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine(nativeDir, "signer-amd64.so");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(nativeDir, "signer-arm64.dylib");
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported");
        }
    }


    /// <summary>
    /// Generates a new API key pair for signing transactions.
    /// </summary>
    /// <param name="seed">Optional seed string for key generation (can be null for random).</param>
    /// <returns>ApiKeyResponse containing privateKey, publicKey, or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern ApiKeyResponse GenerateAPIKey(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? seed);

    /// <summary>
    /// Initializes the signing client with API credentials and network settings.
    /// </summary>
    /// <param name="url">API endpoint URL.</param>
    /// <param name="privateKey">API private key for signing.</param>
    /// <param name="chainId">Blockchain network ID (304 for mainnet, 300 for testnet).</param>
    /// <param name="apiKeyIndex">Index of the API key to use.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>Error message string or null on success.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr CreateClient(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string privateKey,
        int chainId,
        int apiKeyIndex,
        long accountIndex);



    /// <summary>
    /// Signs a single order creation request.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="clientOrderIndex">Client-side unique order ID.</param>
    /// <param name="baseAmount">Order size in base asset units.</param>
    /// <param name="price">Order price (scaled).</param>
    /// <param name="isAsk">1 for sell, 0 for buy.</param>
    /// <param name="orderType">Order type (0-6).</param>
    /// <param name="timeInForce">Time-in-force (0-2).</param>
    /// <param name="reduceOnly">1 to only reduce position, 0 otherwise.</param>
    /// <param name="triggerPrice">Trigger price for conditional orders (0 for none).</param>
    /// <param name="orderExpiry">Unix timestamp or -1 for 28-day default.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignCreateOrder(
        int marketIndex,
        long clientOrderIndex,
        long baseAmount,
        long price,
        int isAsk,
        int orderType,
        int timeInForce,
        int reduceOnly,
        int triggerPrice,
        long orderExpiry,
        long nonce);

    /// <summary>
    /// Signs a grouped orders request (OCO, OTO, OTOCO).
    /// </summary>
    /// <param name="groupingType">1=OTO, 2=OCO, 3=OTOCO.</param>
    /// <param name="orders">Pointer to array of CreateOrderTxReq structs.</param>
    /// <param name="ordersLength">Number of orders in the array.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignCreateGroupedOrders(
        byte groupingType,
        IntPtr orders,
        int ordersLength,
        long nonce);

    /// <summary>
    /// Signs a request to cancel a specific order.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignCancelOrder(
        int marketIndex,
        long orderId,
        long nonce);

    /// <summary>
    /// Signs a request to cancel all orders in a market.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="tif">Time-in-force for cancellation (0=immediate).</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignCancelAllOrders(
        int marketIndex,
        long tif,
        long nonce);

    /// <summary>
    /// Signs a request to modify an existing order.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="orderId">Order ID to modify.</param>
    /// <param name="newClientOrderIndex">New client order index.</param>
    /// <param name="newBaseAmount">New order size.</param>
    /// <param name="newPrice">New order price.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignModifyOrder(
        int marketIndex,
        long orderId,
        long newClientOrderIndex,
        long newBaseAmount,
        long newPrice,
        long nonce);



    /// <summary>
    /// Signs a request to update position leverage settings.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="marginMode">0=cross, 1=isolated.</param>
    /// <param name="leverage">Leverage multiplier.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <returns>StrOrErr containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr SignUpdateLeverage(
        int marketIndex,
        int marginMode,
        int leverage,
        long nonce);

}
