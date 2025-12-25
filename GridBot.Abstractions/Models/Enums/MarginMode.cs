namespace GridBot.Abstractions.Models.Enums;

/// <summary>
/// Specifies the margin mode for trading positions.
/// </summary>
public enum MarginMode
{
    /// <summary>
    /// Cross margin - all positions share the same margin pool.
    /// </summary>
    Cross = 0,

    /// <summary>
    /// Isolated margin - each position has its own dedicated margin.
    /// </summary>
    Isolated = 1
}
