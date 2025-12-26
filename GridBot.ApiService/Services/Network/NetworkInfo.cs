using GridBot.Lighter;

namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Information about an available network.
/// </summary>
/// <param name="Type">The network type.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="IsConfigured">Whether the network has valid credentials configured.</param>
/// <param name="StatusMessage">Optional status message (e.g., connection status or error).</param>
public record NetworkInfo(
    LighterNetworkType Type,
    string DisplayName,
    bool IsConfigured,
    string? StatusMessage);
