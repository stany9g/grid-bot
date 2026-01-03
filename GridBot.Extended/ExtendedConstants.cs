namespace GridBot.Extended;

/// <summary>
/// Constants for the Extended DEX integration.
/// </summary>
public static class ExtendedConstants
{
    /// <summary>
    /// API header name for the API key.
    /// </summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// Standard rate limit: 1,000 requests per minute.
    /// </summary>
    public const int StandardRateLimitPerMinute = 1000;

    /// <summary>
    /// Market maker rate limit: 60,000 requests per 5 minutes.
    /// </summary>
    public const int MarketMakerRateLimitPer5Min = 60000;

    /// <summary>
    /// Safety margin for rate limiting (80% of limit).
    /// </summary>
    public const double RateLimitSafetyMargin = 0.80;

    /// <summary>
    /// Critical threshold for rate limiting (95% of limit).
    /// </summary>
    public const double RateLimitCriticalThreshold = 0.95;

    /// <summary>
    /// Maximum nonce value (2^31 - 1).
    /// </summary>
    public const long MaxNonceValue = 2_147_483_647;

    /// <summary>
    /// Nonce warning threshold (warn when approaching limit).
    /// </summary>
    public const long NonceWarningThreshold = 2_000_000_000;

    /// <summary>
    /// Maximum number of nonce sync retry attempts.
    /// </summary>
    public const int MaxNonceSyncRetries = 5;

    /// <summary>
    /// Maximum pending orders per market before blocking new submissions.
    /// </summary>
    public const int MaxPendingOrdersPerMarket = 50;

    /// <summary>
    /// Threshold to resume order submissions after pending count decreases.
    /// </summary>
    public const int PendingOrdersResumeThreshold = 40;

    /// <summary>
    /// Order confirmation timeout in seconds.
    /// </summary>
    public const int OrderConfirmationTimeoutSeconds = 30;

    /// <summary>
    /// Orphan order age threshold in seconds.
    /// </summary>
    public const int OrphanOrderAgeSeconds = 60;

    /// <summary>
    /// Position reconciliation interval in seconds.
    /// </summary>
    public const int PositionReconciliationIntervalSeconds = 60;

    /// <summary>
    /// Fee precision (4 decimal places, 0.0001 = 0.01%).
    /// </summary>
    public const int FeePrecision = 4;

    /// <summary>
    /// Default taker fee rate for orders (0.025% = 0.00025).
    /// Maker fee is 0%. Use taker fee when creating orders.
    /// </summary>
    public const decimal DefaultFeeRate = 0.00025m;

    /// <summary>
    /// Maximum fee rate before warning (1% = 0.01).
    /// </summary>
    public const decimal MaxFeeRateWarning = 0.01m;
}
