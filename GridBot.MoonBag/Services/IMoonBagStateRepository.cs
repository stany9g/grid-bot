using GridBot.MoonBag.Models;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Abstraction for persisting moon bag state.
/// Implemented by GridBot.ApiService using Redis or other storage.
/// </summary>
public interface IMoonBagStateRepository
{
    /// <summary>
    /// Saves the moon bag status to persistent storage.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="status">Moon bag status to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveMoonBagStatusAsync(int marketId, MoonBagStatus status, CancellationToken ct = default);

    /// <summary>
    /// Loads the moon bag status from persistent storage.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded status, or null if not found.</returns>
    Task<MoonBagStatus?> LoadMoonBagStatusAsync(int marketId, CancellationToken ct = default);
}
