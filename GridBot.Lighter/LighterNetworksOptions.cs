namespace GridBot.Lighter;

/// <summary>
/// Configuration options for multiple Lighter networks.
/// Allows configuring both testnet and mainnet with separate credentials.
/// </summary>
public sealed class LighterNetworksOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "LighterNetworks";

    /// <summary>
    /// The default network to use on startup.
    /// </summary>
    public string DefaultNetwork { get; set; } = "Testnet";

    /// <summary>
    /// Configuration for the testnet network.
    /// </summary>
    public LighterOptions? Testnet { get; set; }

    /// <summary>
    /// Configuration for the mainnet network.
    /// </summary>
    public LighterOptions? Mainnet { get; set; }

    /// <summary>
    /// Gets the configuration for the specified network type.
    /// </summary>
    /// <param name="network">The network type.</param>
    /// <returns>The network configuration, or null if not configured.</returns>
    public LighterOptions? GetNetwork(LighterNetworkType network) => network switch
    {
        LighterNetworkType.Testnet => Testnet,
        LighterNetworkType.Mainnet => Mainnet,
        _ => null
    };

    /// <summary>
    /// Gets the default network type from the DefaultNetwork string.
    /// </summary>
    /// <returns>The parsed network type, defaults to Testnet if invalid.</returns>
    public LighterNetworkType GetDefaultNetworkType()
    {
        if (Enum.TryParse<LighterNetworkType>(DefaultNetwork, ignoreCase: true, out var networkType))
        {
            return networkType;
        }

        return LighterNetworkType.Testnet;
    }

    /// <summary>
    /// Checks if a network is properly configured with valid credentials.
    /// </summary>
    /// <param name="network">The network type to check.</param>
    /// <returns>True if the network has valid configuration.</returns>
    public bool IsNetworkConfigured(LighterNetworkType network)
    {
        var options = GetNetwork(network);
        if (options is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(options.PrivateKey)
            && !string.IsNullOrWhiteSpace(options.ApiUrl)
            && options.AccountIndex > 0
            && options.ChainId > 0;
    }
}
