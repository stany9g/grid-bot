namespace GridBot.Abstractions.Authentication;

/// <summary>
/// Provides authentication and signing capabilities for exchange operations.
/// Different exchanges may use different signing mechanisms (ECDSA, Ed25519, API keys, etc.).
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Gets the account address or identifier associated with these credentials.
    /// </summary>
    string AccountAddress { get; }

    /// <summary>
    /// Gets whether the authentication provider is properly initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Signs a message using the configured credentials.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signature as a byte array.</returns>
    Task<byte[]> SignMessageAsync(byte[] message, CancellationToken ct = default);

    /// <summary>
    /// Signs a message and returns the signature as a hex string.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signature as a hex string.</returns>
    Task<string> SignMessageHexAsync(byte[] message, CancellationToken ct = default);

    /// <summary>
    /// Gets the current nonce for transaction ordering.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current nonce value.</returns>
    Task<long> GetNonceAsync(CancellationToken ct = default);

    /// <summary>
    /// Increments and returns the next nonce for transaction ordering.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next nonce value.</returns>
    Task<long> GetNextNonceAsync(CancellationToken ct = default);

    /// <summary>
    /// Synchronizes the local nonce with the exchange.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the synchronization operation.</returns>
    Task SyncNonceAsync(CancellationToken ct = default);
}
