using GridBot.Extended;

namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Service for managing Extended DEX network selection (testnet/mainnet).
/// Allows runtime switching between networks before the bot starts.
/// </summary>
public interface IExtendedNetworkSelectionService
{
    /// <summary>
    /// Gets the currently selected network type.
    /// </summary>
    ExtendedNetworkType CurrentNetwork { get; }

    /// <summary>
    /// Gets whether a network switch is currently in progress.
    /// Used to prevent bot from starting during network transitions.
    /// </summary>
    bool IsSwitchingNetwork { get; }

    /// <summary>
    /// Gets the list of available networks with their configuration status.
    /// </summary>
    IReadOnlyList<ExtendedNetworkInfo> AvailableNetworks { get; }

    /// <summary>
    /// Event raised when the selected network changes.
    /// </summary>
    event EventHandler<ExtendedNetworkType>? NetworkChanged;

    /// <summary>
    /// Selects a network to use for trading.
    /// </summary>
    /// <param name="network">The network type to select.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is running.</exception>
    /// <exception cref="ArgumentException">Thrown when network is not configured.</exception>
    Task SelectNetworkAsync(ExtendedNetworkType network, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the list of available networks and their status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshNetworkStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// Information about an Extended network's configuration status.
/// </summary>
/// <param name="Type">The network type.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="IsConfigured">Whether the network has valid credentials.</param>
/// <param name="StatusMessage">Additional status information.</param>
public sealed record ExtendedNetworkInfo(
    ExtendedNetworkType Type,
    string DisplayName,
    bool IsConfigured,
    string StatusMessage);
