namespace GridBot.Abstractions.Models.Enums;

/// <summary>
/// Specifies the side of the order (buy or sell).
/// </summary>
public enum OrderSide
{
    /// <summary>
    /// A buy order that opens or increases a long position.
    /// </summary>
    Buy = 0,

    /// <summary>
    /// A sell order that opens or increases a short position.
    /// </summary>
    Sell = 1
}
