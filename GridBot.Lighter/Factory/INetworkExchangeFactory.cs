using GridBot.Abstractions.Factory;

namespace GridBot.Lighter.Factory;

/// <summary>
/// Factory for creating and managing Lighter exchange clients per network.
/// Handles lazy initialization and switching between testnet/mainnet.
/// </summary>
public interface INetworkExchangeFactory
{
    /// <summary>
    /// Creates or retrieves the exchange client for the specified network.
    /// If the client for this network already exists, returns the cached instance.
    /// </summary>
    /// <param name="network">The network type to create a client for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The initialized exchange client for the network.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the network is not configured or initialization fails.
    /// </exception>
    Task<IExchangeClient> CreateForNetworkAsync(LighterNetworkType network, CancellationToken ct = default);

    /// <summary>
    /// Checks if a client for the specified network has been initialized.
    /// </summary>
    /// <param name="network">The network type to check.</param>
    /// <returns>True if the network client is already initialized.</returns>
    bool IsInitialized(LighterNetworkType network);

    /// <summary>
    /// Gets the currently active exchange client, if any.
    /// </summary>
    /// <returns>The current exchange client, or null if none is active.</returns>
    IExchangeClient? GetCurrent();

    /// <summary>
    /// Gets the network type of the currently active client.
    /// </summary>
    /// <returns>The current network type, or null if no client is active.</returns>
    LighterNetworkType? GetCurrentNetwork();

    /// <summary>
    /// Disposes the current exchange client and clears resources.
    /// Must be called before switching to a different network.
    /// </summary>
    /// <returns>A task representing the async disposal operation.</returns>
    Task DisposeCurrentAsync();
}
