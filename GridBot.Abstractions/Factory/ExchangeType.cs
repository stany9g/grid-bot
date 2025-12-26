namespace GridBot.Abstractions.Factory;

/// <summary>
/// Identifies the type of exchange for multi-DEX support.
/// </summary>
public enum ExchangeType
{
    /// <summary>
    /// Lighter DEX - Layer 2 perpetuals exchange.
    /// </summary>
    Lighter = 1,

    /// <summary>
    /// Hyperliquid - High-performance L1 perpetuals exchange.
    /// </summary>
    Hyperliquid = 2,

    /// <summary>
    /// Extended - Starknet-based perpetuals exchange.
    /// </summary>
    Extended = 3
}
