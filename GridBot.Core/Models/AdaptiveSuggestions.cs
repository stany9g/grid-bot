namespace GridBot.Core.Models;

/// <summary>
/// Auto-tuned parameter suggestions calculated from market conditions.
/// These are informational - user can choose to accept or override.
/// </summary>
/// <param name="SuggestedSpacing">Grid spacing percentage based on ATR.</param>
/// <param name="SuggestedBuyLevels">Recommended buy levels.</param>
/// <param name="SuggestedSellLevels">Recommended sell levels.</param>
/// <param name="SuggestedOrderSize">Order size in USDC based on equity and levels.</param>
/// <param name="Reasoning">Human-readable explanation of suggestions.</param>
public sealed record AdaptiveSuggestions(
    decimal SuggestedSpacing,
    int SuggestedBuyLevels,
    int SuggestedSellLevels,
    decimal SuggestedOrderSize,
    string Reasoning)
{
    /// <summary>
    /// Returns an empty suggestions object for error cases.
    /// </summary>
    public static AdaptiveSuggestions Empty(string reason) => new(
        SuggestedSpacing: 0.5m,
        SuggestedBuyLevels: 10,
        SuggestedSellLevels: 10,
        SuggestedOrderSize: 100m,
        Reasoning: reason);
}
