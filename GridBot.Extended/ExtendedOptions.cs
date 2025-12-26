namespace GridBot.Extended;

/// <summary>
/// Configuration options for the Extended DEX client.
/// Bind this from appsettings.json using IConfiguration.
/// </summary>
public sealed class ExtendedOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Extended";

    /// <summary>
    /// API base URL. Defaults to mainnet.
    /// Mainnet: https://api.starknet.extended.exchange/api/v1
    /// Testnet: https://api.starknet.sepolia.extended.exchange/api/v1
    /// </summary>
    public string ApiUrl { get; set; } = "https://api.starknet.extended.exchange/api/v1";

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Stark private key for signing transactions.
    /// IMPORTANT: Keep this secret! Consider using User Secrets or Azure Key Vault in production.
    /// </summary>
    public string StarkPrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Stark public key (hex string starting with 0x).
    /// </summary>
    public string StarkPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Account address on the Extended exchange.
    /// </summary>
    public string AccountAddress { get; set; } = string.Empty;

    /// <summary>
    /// Vault number for the account (used for deposits/withdrawals).
    /// </summary>
    public long VaultNumber { get; set; }

    /// <summary>
    /// Client ID for the account.
    /// </summary>
    public long ClientId { get; set; }

    /// <summary>
    /// Whether this is testnet (affects max order expiry and other behaviors).
    /// </summary>
    public bool IsTestnet { get; set; } = false;

    /// <summary>
    /// Initial nonce value. Set to 0 to sync from server on startup.
    /// </summary>
    public long InitialNonce { get; set; } = 0;

    /// <summary>
    /// When true, disables all order creation/modification/cancellation operations.
    /// Commands are logged but not executed. Useful for testing data feeds.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// User-Agent header value sent with all requests. Required by Extended API.
    /// </summary>
    public string UserAgent { get; set; } = "GridBot/1.0";

    /// <summary>
    /// Whether to use market maker rate limits (60,000/5min vs 1,000/min).
    /// </summary>
    public bool IsMarketMaker { get; set; } = false;

    /// <summary>
    /// Gets the maximum order expiry in days based on the environment.
    /// Mainnet: 90 days, Testnet: 28 days.
    /// </summary>
    public int MaxOrderExpiryDays => IsTestnet ? 28 : 90;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <returns>Error message if invalid, null if valid.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
            return "ApiUrl is required";

        if (string.IsNullOrWhiteSpace(ApiKey))
            return "ApiKey is required";

        if (string.IsNullOrWhiteSpace(StarkPrivateKey))
            return "StarkPrivateKey is required";

        if (string.IsNullOrWhiteSpace(StarkPublicKey))
            return "StarkPublicKey is required";

        if (string.IsNullOrWhiteSpace(AccountAddress))
            return "AccountAddress is required";

        return null;
    }
}
