namespace GridBot.Extended;

/// <summary>
/// Configuration options for multiple Extended DEX networks.
/// Allows configuring both testnet and mainnet with separate credentials.
/// </summary>
public sealed class ExtendedNetworksOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ExtendedNetworks";

    /// <summary>
    /// The default network to use on startup.
    /// </summary>
    public string DefaultNetwork { get; set; } = "Testnet";

    /// <summary>
    /// Configuration for the testnet network (Starknet Sepolia).
    /// </summary>
    public ExtendedOptions? Testnet { get; set; }

    /// <summary>
    /// Configuration for the mainnet network (Starknet).
    /// </summary>
    public ExtendedOptions? Mainnet { get; set; }

    /// <summary>
    /// WebSocket options shared across networks (can be overridden per-network if needed).
    /// </summary>
    public WebSocketOptions WebSocket { get; set; } = new();

    /// <summary>
    /// Gets the configuration for the specified network type.
    /// </summary>
    /// <param name="network">The network type.</param>
    /// <returns>The network configuration, or null if not configured.</returns>
    public ExtendedOptions? GetNetwork(ExtendedNetworkType network) => network switch
    {
        ExtendedNetworkType.Testnet => Testnet,
        ExtendedNetworkType.Mainnet => Mainnet,
        _ => null
    };

    /// <summary>
    /// Gets the default network type from the DefaultNetwork string.
    /// </summary>
    /// <returns>The parsed network type, defaults to Testnet if invalid.</returns>
    public ExtendedNetworkType GetDefaultNetworkType()
    {
        if (Enum.TryParse<ExtendedNetworkType>(DefaultNetwork, ignoreCase: true, out var networkType))
        {
            return networkType;
        }

        return ExtendedNetworkType.Testnet;
    }

    /// <summary>
    /// Checks if a network is properly configured with valid credentials.
    /// </summary>
    /// <param name="network">The network type to check.</param>
    /// <returns>True if the network has valid configuration.</returns>
    public bool IsNetworkConfigured(ExtendedNetworkType network)
    {
        var options = GetNetwork(network);
        if (options is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(options.ApiKey)
            && !string.IsNullOrWhiteSpace(options.ApiUrl)
            && !string.IsNullOrWhiteSpace(options.StarkPrivateKey)
            && !string.IsNullOrWhiteSpace(options.StarkPublicKey)
            && !string.IsNullOrWhiteSpace(options.AccountAddress);
    }
}
