namespace GridBot.Lighter.Models;

/// <summary>
/// Transaction type constants for the Lighter API.
/// Each operation type has a unique numeric identifier (1-64 range).
/// </summary>
public static class TransactionTypes
{
    /// <summary>
    /// Change public key transaction (type 0).
    /// </summary>
    public const int ChangePublicKey = 0;

    /// <summary>
    /// Create order transaction (type 1).
    /// </summary>
    public const int CreateOrder = 1;

    /// <summary>
    /// Cancel order transaction (type 2).
    /// </summary>
    public const int CancelOrder = 2;

    /// <summary>
    /// Cancel all orders transaction (type 3).
    /// </summary>
    public const int CancelAllOrders = 3;

    /// <summary>
    /// Modify order transaction (type 4).
    /// </summary>
    public const int ModifyOrder = 4;

    /// <summary>
    /// Withdraw transaction (type 5).
    /// </summary>
    public const int Withdraw = 5;

    /// <summary>
    /// Transfer transaction (type 6).
    /// </summary>
    public const int Transfer = 6;

    /// <summary>
    /// Update leverage transaction (type 7).
    /// </summary>
    public const int UpdateLeverage = 7;

    /// <summary>
    /// Create grouped orders transaction (type 8).
    /// Used for OCO, OTO, OTOCO order groups.
    /// </summary>
    public const int CreateGroupedOrders = 8;
}
