namespace GridBot.Lighter;

/// <summary>
/// Configuration options for the Lighter client.
/// Bind this from appsettings.json using IConfiguration.
/// </summary>
public sealed class LighterOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Lighter";

    /// <summary>
    /// API base URL. Defaults to mainnet.
    /// </summary>
    public string ApiUrl { get; set; } = "https://mainnet.zklighter.elliot.ai";

    /// <summary>
    /// API private key for signing transactions.
    /// IMPORTANT: Keep this secret! Consider using User Secrets or Azure Key Vault in production.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Chain ID. Use 304 for mainnet, 300 for testnet.
    /// </summary>
    public int ChainId { get; set; } = 304;

    /// <summary>
    /// API key index for the account.
    /// </summary>
    public int ApiKeyIndex { get; set; } = 0;

    /// <summary>
    /// Account index on the Lighter protocol.
    /// </summary>
    public long AccountIndex { get; set; }

    /// <summary>
    /// Initial nonce value. Set to 0 to sync from server on startup.
    /// </summary>
    public long InitialNonce { get; set; } = 0;

    /// <summary>
    /// When true, disables all order creation/modification/cancellation operations.
    /// Commands are logged but not executed. Useful for testing WebSocket data feeds.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <returns>Error message if invalid, null if valid.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
            return "ApiUrl is required";

        if (string.IsNullOrWhiteSpace(PrivateKey))
            return "PrivateKey is required";

        if (AccountIndex <= 0)
            return "AccountIndex must be greater than 0";

        if (ChainId != Models.ChainId.Mainnet && ChainId != Models.ChainId.Testnet)
            return $"ChainId must be {Models.ChainId.Mainnet} (mainnet) or {Models.ChainId.Testnet} (testnet)";

        return null;
    }
}
