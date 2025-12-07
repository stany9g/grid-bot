using System.Runtime.InteropServices;

namespace GridBot.Lighter.Native;

/// <summary>
/// Response structure for API key generation operations.
/// Maps to the Go struct returned by GenerateAPIKey.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ApiKeyResponse
{
    /// <summary>
    /// Pointer to the generated private key string (UTF-8).
    /// </summary>
    public IntPtr PrivateKey;

    /// <summary>
    /// Pointer to the generated public key string (UTF-8).
    /// </summary>
    public IntPtr PublicKey;

    /// <summary>
    /// Pointer to error message string (UTF-8) if operation failed, otherwise IntPtr.Zero.
    /// </summary>
    public IntPtr Err;
}

/// <summary>
/// Generic response structure for operations that return a string result or error.
/// Maps to the Go StrOrErr struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StrOrErr
{
    /// <summary>
    /// Pointer to the result string (UTF-8) - typically transaction info or signed data.
    /// </summary>
    public IntPtr Str;

    /// <summary>
    /// Pointer to error message string (UTF-8) if operation failed, otherwise IntPtr.Zero.
    /// </summary>
    public IntPtr Err;
}

/// <summary>
/// Response structure for transaction signing operations.
/// Maps to the Go SignedTxResponse struct returned by all Sign* functions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SignedTxResponse
{
    /// <summary>
    /// Transaction type identifier.
    /// </summary>
    public byte TxType;

    /// <summary>
    /// Pointer to transaction info JSON string (UTF-8).
    /// </summary>
    public IntPtr TxInfo;

    /// <summary>
    /// Pointer to transaction hash string (UTF-8).
    /// </summary>
    public IntPtr TxHash;

    /// <summary>
    /// Pointer to the message that was signed (UTF-8).
    /// </summary>
    public IntPtr MessageToSign;

    /// <summary>
    /// Pointer to error message string (UTF-8) if operation failed, otherwise IntPtr.Zero.
    /// </summary>
    public IntPtr Err;
}

/// <summary>
/// Request structure for creating orders in grouped order operations.
/// Maps to the CreateOrderTxReq struct expected by SignCreateGroupedOrders.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CreateOrderTxReq
{
    /// <summary>
    /// Market identifier (e.g., 0 for BTC-USDC).
    /// </summary>
    public byte MarketIndex;

    /// <summary>
    /// Client-side unique order identifier.
    /// </summary>
    public long ClientOrderIndex;

    /// <summary>
    /// Order size in base asset units (scaled).
    /// </summary>
    public long BaseAmount;

    /// <summary>
    /// Order price (scaled according to market tick size).
    /// </summary>
    public uint Price;

    /// <summary>
    /// Order side: 1 for sell (ask), 0 for buy (bid).
    /// </summary>
    public byte IsAsk;

    /// <summary>
    /// Order type: 0=Limit, 1=Market, 2=StopLoss, etc.
    /// </summary>
    public byte Type;

    /// <summary>
    /// Time-in-force: 0=IOC, 1=GTT, 2=PostOnly.
    /// </summary>
    public byte TimeInForce;

    /// <summary>
    /// 1 if order should only reduce position size, 0 otherwise.
    /// </summary>
    public byte ReduceOnly;

    /// <summary>
    /// Trigger price for stop/take-profit orders (0 if not applicable).
    /// </summary>
    public uint TriggerPrice;

    /// <summary>
    /// Order expiry timestamp (Unix), or -1 for default 28-day expiry.
    /// </summary>
    public long OrderExpiry;
}
