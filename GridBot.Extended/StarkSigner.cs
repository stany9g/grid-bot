using GridBot.Extended.Models.Api;
using GridBot.Extended.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// High-level Stark signature client for Extended DEX order signing.
/// Uses Poseidon hash with SNIP-12 typed structured data (same as Python SDK).
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
    /// Signs an order for the Extended DEX using SNIP-12 typed structured data with Poseidon hash.
    /// </summary>
    /// <param name="order">The order parameters to sign.</param>
    /// <param name="marketInfo">Market L2 configuration for asset IDs.</param>
    /// <param name="isTestnet">Whether this is testnet (affects domain chain ID).</param>
    /// <returns>The signature components (R, S).</returns>
    public (string R, string S) SignOrder(StarkExOrderParams order, L2ConfigInfo marketInfo, bool isTestnet = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        // Compute message hash using native library (Poseidon + SNIP-12)
        var messageHash = ComputeOrderHash(order, marketInfo, isTestnet);

        _logger.LogDebug(
            "Signing order: PositionId={PositionId}, BaseAmount={BaseAmount}, QuoteAmount={QuoteAmount}, Fee={Fee}, Nonce={Nonce}, Hash={Hash}",
            order.PositionId, order.BaseAmount, order.QuoteAmount, order.FeeAmount, order.Nonce, TruncateKey(messageHash));

        // Sign the message hash
        var (r, s) = StarkNativeMethods.Sign(_starkPrivateKey, messageHash);

        _logger.LogDebug("Order signed: R={R}, S={S}", TruncateKey(r), TruncateKey(s));

        return (r, s);
    }

    /// <summary>
    /// Computes the Extended DEX order hash using SNIP-12 typed structured data.
    /// Uses the same algorithm as the Python SDK (Poseidon hash).
    /// </summary>
    private string ComputeOrderHash(StarkExOrderParams order, L2ConfigInfo marketInfo, bool isTestnet)
    {
        // Validate required L2 config fields
        if (string.IsNullOrEmpty(marketInfo.SyntheticId))
        {
            throw new ArgumentException("L2Config SyntheticId is required for order signing", nameof(marketInfo));
        }
        if (string.IsNullOrEmpty(marketInfo.CollateralId))
        {
            throw new ArgumentException("L2Config CollateralId is required for order signing", nameof(marketInfo));
        }

        // Domain parameters for Extended DEX
        const string domainName = "Perpetuals";
        const string domainVersion = "v0";
        const string domainRevision = "1";
        var domainChainId = isTestnet ? "SN_SEPOLIA" : "SN_MAIN";

        _logger.LogDebug(
            "Computing order hash: positionId={PositionId}, baseAssetId={BaseAssetId}, " +
            "baseAmount={BaseAmount}, quoteAssetId={QuoteAssetId}, quoteAmount={QuoteAmount}, " +
            "feeAssetId={FeeAssetId}, feeAmount={FeeAmount}, expiration={Exp}, nonce={Nonce}, " +
            "publicKey={PublicKey}, chainId={ChainId}",
            order.PositionId, marketInfo.SyntheticId, order.BaseAmount,
            marketInfo.CollateralId, order.QuoteAmount, marketInfo.CollateralId,
            order.FeeAmount, order.ExpirationSeconds, order.Nonce,
            TruncateKey(_starkPublicKey!), domainChainId);

        // Call native library to compute hash using Poseidon + SNIP-12
        var hash = StarkNativeMethods.GetOrderHash(
            positionId: order.PositionId.ToString(),
            baseAssetIdHex: marketInfo.SyntheticId,
            baseAmount: order.BaseAmount.ToString(),
            quoteAssetIdHex: marketInfo.CollateralId,
            quoteAmount: order.QuoteAmount.ToString(),
            feeAssetIdHex: marketInfo.CollateralId,  // Fee asset = collateral
            feeAmount: order.FeeAmount.ToString(),
            expiration: order.ExpirationSeconds.ToString(),
            salt: order.Nonce.ToString(),
            userPublicKeyHex: _starkPublicKey!,
            domainName: domainName,
            domainVersion: domainVersion,
            domainChainId: domainChainId,
            domainRevision: domainRevision);

        _logger.LogDebug("Computed order hash: {Hash}", TruncateKey(hash));

        return hash;
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
/// Parameters for Extended DEX order signing.
/// </summary>
public sealed class StarkExOrderParams
{
    /// <summary>
    /// Position/vault ID from account L2Vault.
    /// </summary>
    public required long PositionId { get; init; }

    /// <summary>
    /// Synthetic (base) amount in Stark units.
    /// Positive for BUY, negative for SELL.
    /// </summary>
    public required long BaseAmount { get; init; }

    /// <summary>
    /// Collateral (quote) amount in Stark units.
    /// Negative for BUY, positive for SELL.
    /// </summary>
    public required long QuoteAmount { get; init; }

    /// <summary>
    /// Max fee amount in Stark units (always positive).
    /// </summary>
    public required long FeeAmount { get; init; }

    /// <summary>
    /// Nonce/salt for the order.
    /// </summary>
    public required long Nonce { get; init; }

    /// <summary>
    /// Expiration timestamp in Unix seconds (with 14-day buffer).
    /// </summary>
    public required long ExpirationSeconds { get; init; }

    /// <summary>
    /// Whether this is a BUY order.
    /// </summary>
    public bool IsBuy => BaseAmount > 0;
}

/// <summary>
/// Helper for calculating Stark amounts.
/// </summary>
public static class StarkAmountCalculator
{
    /// <summary>
    /// Calculates Stark amounts for an order.
    /// </summary>
    /// <param name="quantity">Human-readable quantity (e.g., 0.001 BTC).</param>
    /// <param name="price">Price per unit.</param>
    /// <param name="feeRate">Fee rate (e.g., 0.00025 for 0.025%).</param>
    /// <param name="isBuy">Whether this is a BUY order.</param>
    /// <param name="syntheticResolution">Synthetic asset resolution (e.g., 1,000,000).</param>
    /// <param name="collateralResolution">Collateral asset resolution (e.g., 1,000,000).</param>
    /// <returns>Tuple of (baseAmount, quoteAmount, feeAmount) in Stark units.</returns>
    public static (long BaseAmount, long QuoteAmount, long FeeAmount) CalculateStarkAmounts(
        decimal quantity,
        decimal price,
        decimal feeRate,
        bool isBuy,
        long syntheticResolution,
        long collateralResolution)
    {
        // Calculate human amounts
        var collateralValue = quantity * price;
        var feeValue = feeRate * collateralValue;

        // Convert to Stark amounts with appropriate rounding
        // BUY: round UP (pay more), SELL: round DOWN (receive less)
        var baseAmount = isBuy
            ? (long)Math.Ceiling(quantity * syntheticResolution)
            : (long)Math.Floor(quantity * syntheticResolution);

        var quoteAmount = isBuy
            ? (long)Math.Ceiling(collateralValue * collateralResolution)
            : (long)Math.Floor(collateralValue * collateralResolution);

        // Fee always rounds UP
        var feeAmount = (long)Math.Ceiling(feeValue * collateralResolution);

        // Apply sign convention
        // BUY: positive base (receive synthetic), negative quote (pay collateral)
        // SELL: negative base (pay synthetic), positive quote (receive collateral)
        if (isBuy)
        {
            quoteAmount = -quoteAmount;
        }
        else
        {
            baseAmount = -baseAmount;
        }

        return (baseAmount, quoteAmount, feeAmount);
    }

    /// <summary>
    /// Calculates settlement expiration (order expiry + 14 days, in seconds).
    /// </summary>
    /// <param name="orderExpiryMillis">Order expiry in Unix milliseconds.</param>
    /// <returns>Settlement expiration in Unix seconds.</returns>
    public static long CalcSettlementExpiration(long orderExpiryMillis)
    {
        // Add 14 days buffer
        const long fourteenDaysMillis = 14L * 24 * 60 * 60 * 1000;
        var withBuffer = orderExpiryMillis + fourteenDaysMillis;

        // Convert to seconds (ceiling)
        return (long)Math.Ceiling(withBuffer / 1000.0);
    }

    /// <summary>
    /// Generates a random 32-bit nonce.
    /// </summary>
    /// <returns>Random nonce value.</returns>
    public static long GenerateNonce()
    {
        return Random.Shared.NextInt64(1, uint.MaxValue);
    }
}
