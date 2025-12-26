namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Request model for selecting a network.
/// </summary>
/// <param name="NetworkType">The network type to select (e.g., "Testnet", "Mainnet").</param>
public record NetworkSelectRequest(string NetworkType);
