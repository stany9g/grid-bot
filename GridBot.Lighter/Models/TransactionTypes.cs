namespace GridBot.Lighter.Models;

/// <summary>
/// Transaction type constants for the Lighter API.
/// Values from official Python SDK.
/// </summary>
public static class TransactionTypes
{
    /// <summary>
    /// Change public key transaction (type 8).
    /// </summary>
    public const int ChangePublicKey = 8;

    /// <summary>
    /// Create sub account transaction (type 9).
    /// </summary>
    public const int CreateSubAccount = 9;

    /// <summary>
    /// Create public pool transaction (type 10).
    /// </summary>
    public const int CreatePublicPool = 10;

    /// <summary>
    /// Update public pool transaction (type 11).
    /// </summary>
    public const int UpdatePublicPool = 11;

    /// <summary>
    /// Transfer transaction (type 12).
    /// </summary>
    public const int Transfer = 12;

    /// <summary>
    /// Withdraw transaction (type 13).
    /// </summary>
    public const int Withdraw = 13;

    /// <summary>
    /// Create order transaction (type 14).
    /// </summary>
    public const int CreateOrder = 14;

    /// <summary>
    /// Cancel order transaction (type 15).
    /// </summary>
    public const int CancelOrder = 15;

    /// <summary>
    /// Cancel all orders transaction (type 16).
    /// </summary>
    public const int CancelAllOrders = 16;

    /// <summary>
    /// Modify order transaction (type 17).
    /// </summary>
    public const int ModifyOrder = 17;

    /// <summary>
    /// Mint shares transaction (type 18).
    /// </summary>
    public const int MintShares = 18;

    /// <summary>
    /// Burn shares transaction (type 19).
    /// </summary>
    public const int BurnShares = 19;

    /// <summary>
    /// Update leverage transaction (type 20).
    /// </summary>
    public const int UpdateLeverage = 20;

    /// <summary>
    /// Create grouped orders transaction (type 28).
    /// Used for OCO, OTO, OTOCO order groups.
    /// </summary>
    public const int CreateGroupedOrders = 28;
}
