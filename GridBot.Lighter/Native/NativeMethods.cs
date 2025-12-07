using System.Runtime.InteropServices;

namespace GridBot.Lighter.Native;

/// <summary>
/// P/Invoke declarations for the Lighter native signing library.
/// Handles platform-specific library loading and marshaling between C# and native code.
/// </summary>
internal static partial class NativeMethods
{
    private const string LibraryName = "lighter-signer-windows-amd64";
    private static IntPtr _loadedLibrary = IntPtr.Zero;
    private static string? _loadError;

    /// <summary>
    /// Static constructor to configure native library loading based on platform.
    /// </summary>
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    /// <summary>
    /// Validates that the native library can be loaded. Call this at startup to catch issues early.
    /// </summary>
    /// <returns>Null if successful, error message if library cannot be loaded.</returns>
    public static string? ValidateNativeLibrary()
    {
        try
        {
            var libPath = GetNativeLibraryPath();

            Console.WriteLine($"[NativeLibrary] Validating native library...");
            Console.WriteLine($"[NativeLibrary] Platform: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"[NativeLibrary] Architecture: {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"[NativeLibrary] Library path: {libPath}");

            if (!File.Exists(libPath))
            {
                return $"Native library not found at: {libPath}";
            }

            var fileInfo = new FileInfo(libPath);
            Console.WriteLine($"[NativeLibrary] File size: {fileInfo.Length} bytes");

            // Try to load the library
            if (_loadedLibrary == IntPtr.Zero)
            {
                _loadedLibrary = NativeLibrary.Load(libPath);
            }

            if (_loadedLibrary == IntPtr.Zero)
            {
                return "NativeLibrary.Load returned null pointer";
            }

            Console.WriteLine($"[NativeLibrary] Library loaded successfully at 0x{_loadedLibrary:X}");
            return null;
        }
        catch (DllNotFoundException ex)
        {
            _loadError = $"DllNotFoundException: {ex.Message}. This usually means a dependency is missing (e.g., libstdc++, libgcc).";
            Console.WriteLine($"[NativeLibrary] ERROR: {_loadError}");
            return _loadError;
        }
        catch (BadImageFormatException ex)
        {
            _loadError = $"BadImageFormatException: {ex.Message}. The library was compiled for a different architecture (expected: {RuntimeInformation.ProcessArchitecture}).";
            Console.WriteLine($"[NativeLibrary] ERROR: {_loadError}");
            return _loadError;
        }
        catch (Exception ex)
        {
            _loadError = $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[NativeLibrary] ERROR: {_loadError}");
            return _loadError;
        }
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

        // Return cached library if already loaded
        if (_loadedLibrary != IntPtr.Zero)
        {
            return _loadedLibrary;
        }

        string libPath = GetNativeLibraryPath();

        if (!File.Exists(libPath))
        {
            throw new FileNotFoundException($"Native library not found at: {libPath}");
        }

        _loadedLibrary = NativeLibrary.Load(libPath);
        return _loadedLibrary;
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
            return Path.Combine(nativeDir, "lighter-signer-windows-amd64.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Check architecture for Linux
            var arch = RuntimeInformation.ProcessArchitecture;
            if (arch == Architecture.Arm64)
            {
                return Path.Combine(nativeDir, "signer-arm64.so");
            }
            else if (arch == Architecture.X64)
            {
                // Fall back to arm64 if amd64 not available (for compatibility)
                var amd64Path = Path.Combine(nativeDir, "signer-amd64.so");
                if (File.Exists(amd64Path))
                {
                    return amd64Path;
                }
                Console.WriteLine($"[NativeLibrary] WARNING: Running on x64 but signer-amd64.so not found, trying arm64.so");
                return Path.Combine(nativeDir, "signer-arm64.so");
            }
            else
            {
                throw new PlatformNotSupportedException($"Linux architecture {arch} is not supported. Supported: x64, arm64");
            }
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
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignCreateOrder(
        int marketIndex,
        long clientOrderIndex,
        long baseAmount,
        int price,
        int isAsk,
        int orderType,
        int timeInForce,
        int reduceOnly,
        int triggerPrice,
        long orderExpiry,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Signs a grouped orders request (OCO, OTO, OTOCO).
    /// </summary>
    /// <param name="groupingType">1=OTO, 2=OCO, 3=OTOCO.</param>
    /// <param name="orders">Pointer to array of CreateOrderTxReq structs.</param>
    /// <param name="ordersLength">Number of orders in the array.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignCreateGroupedOrders(
        byte groupingType,
        IntPtr orders,
        int ordersLength,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Signs a request to cancel a specific order.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="orderIndex">Order index to cancel.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignCancelOrder(
        int marketIndex,
        long orderIndex,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Signs a request to cancel all orders.
    /// </summary>
    /// <param name="timeInForce">Time-in-force for cancellation.</param>
    /// <param name="time">Timestamp for cancellation.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignCancelAllOrders(
        int timeInForce,
        long time,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Signs a request to modify an existing order.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="orderIndex">Order index to modify.</param>
    /// <param name="baseAmount">New order size.</param>
    /// <param name="price">New order price.</param>
    /// <param name="triggerPrice">New trigger price.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignModifyOrder(
        int marketIndex,
        long orderIndex,
        long baseAmount,
        long price,
        long triggerPrice,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Signs a request to update position leverage settings.
    /// </summary>
    /// <param name="marketIndex">Market identifier.</param>
    /// <param name="initialMarginFraction">Initial margin fraction.</param>
    /// <param name="marginMode">0=cross, 1=isolated.</param>
    /// <param name="nonce">Transaction nonce.</param>
    /// <param name="apiKeyIndex">API key index.</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>SignedTxResponse containing transaction info or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern SignedTxResponse SignUpdateLeverage(
        int marketIndex,
        int initialMarginFraction,
        int marginMode,
        long nonce,
        int apiKeyIndex,
        long accountIndex);

    /// <summary>
    /// Creates an authentication token for accessing private API endpoints.
    /// </summary>
    /// <param name="deadline">Unix timestamp when the token expires (now + validity_seconds).</param>
    /// <param name="apiKeyIndex">Index of the API key (0-254).</param>
    /// <param name="accountIndex">Account identifier.</param>
    /// <returns>StrOrErr containing the auth token or error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern StrOrErr CreateAuthToken(
        long deadline,
        int apiKeyIndex,
        long accountIndex);

}
