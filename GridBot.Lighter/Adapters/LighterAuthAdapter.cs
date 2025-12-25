using GridBot.Abstractions.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="SignerClient"/> and <see cref="ILighterCommandClient"/>
/// to the <see cref="IAuthenticationProvider"/> interface.
/// </summary>
internal sealed class LighterAuthAdapter : IAuthenticationProvider
{
    private readonly SignerClient _signerClient;
    private readonly ILighterCommandClient _commandClient;
    private readonly LighterOptions _options;
    private readonly ILogger<LighterAuthAdapter> _logger;
    private long _nonce;
    private readonly object _nonceLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterAuthAdapter"/> class.
    /// </summary>
    /// <param name="signerClient">The Lighter signer client.</param>
    /// <param name="commandClient">The Lighter command client for nonce sync.</param>
    /// <param name="options">Lighter configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterAuthAdapter(
        SignerClient signerClient,
        ILighterCommandClient commandClient,
        IOptions<LighterOptions> options,
        ILogger<LighterAuthAdapter> logger)
    {
        _signerClient = signerClient ?? throw new ArgumentNullException(nameof(signerClient));
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string AccountAddress => _options.AccountIndex.ToString();

    /// <inheritdoc />
    public bool IsInitialized => true; // SignerClient is initialized during DI registration

    /// <inheritdoc />
    public Task<byte[]> SignMessageAsync(byte[] message, CancellationToken ct = default)
    {
        // Lighter's native signer handles signing internally for each transaction type
        // This method would need to be implemented if we expose raw signing
        // For now, throw as it's not used by the grid bot
        throw new NotSupportedException(
            "Raw message signing is not supported. Use the order/transaction methods instead.");
    }

    /// <inheritdoc />
    public async Task<string> SignMessageHexAsync(byte[] message, CancellationToken ct = default)
    {
        var bytes = await SignMessageAsync(message, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public Task<long> GetNonceAsync(CancellationToken ct = default)
    {
        lock (_nonceLock)
        {
            return Task.FromResult(_nonce);
        }
    }

    /// <inheritdoc />
    public Task<long> GetNextNonceAsync(CancellationToken ct = default)
    {
        lock (_nonceLock)
        {
            return Task.FromResult(++_nonce);
        }
    }

    /// <inheritdoc />
    public async Task SyncNonceAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Syncing nonce for account {AccountIndex}, API key {ApiKeyIndex}",
            _options.AccountIndex, _options.ApiKeyIndex);

        try
        {
            var syncedNonce = await _commandClient.SyncNonceAsync(
                _options.AccountIndex,
                _options.ApiKeyIndex,
                ct);

            lock (_nonceLock)
            {
                // SyncNonceAsync returns the NEXT nonce to use
                // We store one less so GetNextNonceAsync returns the correct value
                _nonce = syncedNonce - 1;
            }

            _logger.LogInformation("Nonce synced successfully: {Nonce}", syncedNonce);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync nonce");
            throw;
        }
    }

    /// <summary>
    /// Creates an authentication token for accessing private API endpoints.
    /// </summary>
    /// <param name="validitySeconds">How long the token should be valid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The auth token, or null if creation failed.</returns>
    public async Task<string?> CreateAuthTokenAsync(int validitySeconds = 600, CancellationToken ct = default)
    {
        var (authToken, error) = await _commandClient.CreateAuthTokenAsync(validitySeconds);

        if (error != null)
        {
            _logger.LogWarning("Failed to create auth token: {Error}", error);
            return null;
        }

        return authToken;
    }
}
