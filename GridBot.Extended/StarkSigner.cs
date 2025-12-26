using System.Globalization;
using System.Numerics;
using GridBot.Extended.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// High-level Stark signature client for Extended DEX order signing.
/// Handles message hash construction and signature generation.
/// </summary>
public sealed class StarkSigner : IDisposable
{
    private readonly ExtendedOptions _options;
    private readonly ILogger<StarkSigner> _logger;
    private readonly string _starkPrivateKey;
    private string? _starkPublicKey;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Gets whether the signer is initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the Stark public key (derived from private key).
    /// </summary>
    public string? StarkPublicKey => _starkPublicKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="StarkSigner"/> class.
    /// </summary>
    public StarkSigner(IOptions<ExtendedOptions> options, ILogger<StarkSigner> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _starkPrivateKey = _options.StarkPrivateKey;

        if (string.IsNullOrWhiteSpace(_starkPrivateKey))
        {
            throw new InvalidOperationException("StarkPrivateKey is required for signing operations");
        }
    }

    /// <summary>
    /// Validates that the native signing library is available.
    /// </summary>
    /// <returns>Null if valid, error message otherwise.</returns>
    public static string? ValidateNativeLibrary()
    {
        return StarkNativeMethods.ValidateNativeLibrary();
    }

    /// <summary>
    /// Initializes the signer by validating the native library and deriving the public key.
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isInitialized)
        {
            return;
        }

        var validationError = ValidateNativeLibrary();
        if (validationError != null)
        {
            throw new InvalidOperationException($"Native library validation failed: {validationError}");
        }

        // Derive public key from private key
        _starkPublicKey = StarkNativeMethods.GetPublicKey(_starkPrivateKey);
        _isInitialized = true;

        _logger.LogInformation(
            "StarkSigner initialized. Public key: {PublicKey}",
            TruncateKey(_starkPublicKey));
    }

    /// <summary>
    /// Signs an order for the Extended DEX.
    /// </summary>
    /// <param name="order">The order parameters to sign.</param>
    /// <returns>The signature components (R, S).</returns>
    public (string R, string S) SignOrder(ExtendedOrderMessage order)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        // Compute message hash using Pedersen hash chain
        var messageHash = ComputeOrderMessageHash(order);

        _logger.LogDebug(
            "Signing order: Market={Market}, Side={Side}, Qty={Qty}, Price={Price}, Nonce={Nonce}",
            order.Market, order.Side, order.Quantity, order.Price, order.Nonce);

        // Sign the message hash
        var (r, s) = StarkNativeMethods.Sign(_starkPrivateKey, messageHash);

        _logger.LogDebug("Order signed: R={R}, S={S}", TruncateKey(r), TruncateKey(s));

        return (r, s);
    }

    /// <summary>
    /// Computes the Pedersen hash for an order message.
    /// Extended DEX uses SNIP-12 style typed data hashing.
    /// </summary>
    private string ComputeOrderMessageHash(ExtendedOrderMessage order)
    {
        // Extended order hash structure (SNIP-12 compatible):
        // hash = pedersen(pedersen(pedersen(pedersen(pedersen(
        //   domain_hash,
        //   stark_key),
        //   market_hash),
        //   order_params_hash),
        //   nonce),
        //   expiry)

        // Step 1: Domain separator (Extended DEX specific)
        var domainHash = ComputeDomainHash();

        // Step 2: Hash with stark key
        var hash1 = StarkNativeMethods.PedersenHash(domainHash, _starkPublicKey!);

        // Step 3: Market identifier hash
        var marketHash = ComputeStringHash(order.Market);
        var hash2 = StarkNativeMethods.PedersenHash(hash1, marketHash);

        // Step 4: Order parameters hash
        var paramsHash = ComputeOrderParamsHash(order);
        var hash3 = StarkNativeMethods.PedersenHash(hash2, paramsHash);

        // Step 5: Nonce
        var nonceHex = ToFelt(order.Nonce);
        var hash4 = StarkNativeMethods.PedersenHash(hash3, nonceHex);

        // Step 6: Expiry
        var expiryHex = ToFelt(order.ExpiryEpochMillis);
        var finalHash = StarkNativeMethods.PedersenHash(hash4, expiryHex);

        return finalHash;
    }

    /// <summary>
    /// Computes the domain separator hash for Extended DEX.
    /// </summary>
    private string ComputeDomainHash()
    {
        // Domain: "Extended DEX" + chain ID + version
        var domainName = ComputeStringHash("Extended DEX");
        var chainId = ToFelt(_options.IsTestnet ? 300 : 304);
        var version = ToFelt(1);

        return StarkNativeMethods.PedersenHashMany(domainName, chainId, version);
    }

    /// <summary>
    /// Computes the hash of order parameters.
    /// </summary>
    private string ComputeOrderParamsHash(ExtendedOrderMessage order)
    {
        var sideValue = order.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        var typeValue = order.Type.Equals("market", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        // Convert decimal values to scaled integers
        var priceScaled = ScaleDecimal(order.Price, 8); // 8 decimal places
        var qtyScaled = ScaleDecimal(order.Quantity, 8);
        var feeScaled = ScaleDecimal(order.Fee, 8);

        return StarkNativeMethods.PedersenHashMany(
            ToFelt(sideValue),
            ToFelt(typeValue),
            ToFelt(priceScaled),
            ToFelt(qtyScaled),
            ToFelt(feeScaled),
            ToFelt(order.ReduceOnly ? 1 : 0)
        );
    }

    /// <summary>
    /// Computes Pedersen hash of a string (as felt array).
    /// </summary>
    private static string ComputeStringHash(string value)
    {
        // Convert string to felt representation (each char as felt, then hash chain)
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = "0x0";

        foreach (var b in bytes)
        {
            hash = StarkNativeMethods.PedersenHash(hash, ToFelt(b));
        }

        return hash;
    }

    /// <summary>
    /// Converts a long value to a hex felt string.
    /// </summary>
    private static string ToFelt(long value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Felt values must be non-negative", nameof(value));
        }
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a BigInteger to a hex felt string.
    /// </summary>
    private static string ToFelt(BigInteger value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Felt values must be non-negative", nameof(value));
        }
        return $"0x{value:x}";
    }

    /// <summary>
    /// Scales a decimal value to an integer with the specified precision.
    /// </summary>
    private static BigInteger ScaleDecimal(decimal value, int decimals)
    {
        var scale = (decimal)Math.Pow(10, decimals);
        return new BigInteger(Math.Floor(value * scale));
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StarkSigner not initialized. Call Initialize() first.");
        }
    }

    private static string TruncateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 16)
        {
            return key;
        }
        return $"{key[..10]}...{key[^6..]}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isInitialized = false;
    }
}

/// <summary>
/// Order message structure for signing.
/// </summary>
public sealed class ExtendedOrderMessage
{
    /// <summary>
    /// Market identifier (e.g., "BTC-USD-PERP").
    /// </summary>
    public required string Market { get; init; }

    /// <summary>
    /// Order side: "BUY" or "SELL".
    /// </summary>
    public required string Side { get; init; }

    /// <summary>
    /// Order type: "limit" or "market".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Order price (for limit orders).
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Order quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// Fee rate.
    /// </summary>
    public required decimal Fee { get; init; }

    /// <summary>
    /// Order nonce.
    /// </summary>
    public required long Nonce { get; init; }

    /// <summary>
    /// Order expiry in epoch milliseconds.
    /// </summary>
    public required long ExpiryEpochMillis { get; init; }

    /// <summary>
    /// Whether the order is reduce-only.
    /// </summary>
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Time-in-force setting.
    /// </summary>
    public string? TimeInForce { get; init; }
}
