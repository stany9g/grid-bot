# Lighter DEX accountActiveOrders Authentication Specification

## Problem Statement

The `accountActiveOrders` endpoint is returning:
```json
{"code":20001,"message":"invalid param: : auth query param and Authorization header are both empty"}
```

This indicates the endpoint **requires authentication** via either:
1. The `auth` query parameter, OR
2. The `Authorization` HTTP header

## Authentication Overview

### Two Authentication Methods Supported

The Lighter API supports two ways to pass authentication:

| Method | Location | Parameter Name | Value Format |
|--------|----------|----------------|--------------|
| Query Parameter | URL | `auth` | `{auth_token}` |
| HTTP Header | Header | `Authorization` | `{auth_token}` |

**Note:** You must use ONE of these methods. The error occurs when BOTH are empty.

## Auth Token Generation

### What is the Auth Token?

The auth token is a signed string generated using the native signer library (`lighter-signer-*.dll/so/dylib`). It contains:
- An expiration deadline (Unix timestamp)
- The account index
- The API key index
- A cryptographic signature proving ownership of the private key

### Native Function Signature

```c
// From lighter-signer header file
StrOrErr CreateAuthToken(
    long long int cDeadline,     // Absolute expiration timestamp (Unix seconds)
    int cApiKeyIndex,            // API key index (typically 0)
    long long int cAccountIndex  // Account index
);
```

### Parameters Explained

| Parameter | Type | Description |
|-----------|------|-------------|
| `cDeadline` | `long long int` | Absolute expiration time in Unix seconds. Calculated as `current_time + validity_period`. Typical validity is 600 seconds (10 minutes). |
| `cApiKeyIndex` | `int` | The API key index used for signing. This must match the key index used when creating the client. Values 0-254 are valid (0=desktop, 1=mobile, 2-254=custom). |
| `cAccountIndex` | `long long int` | The Lighter account index. This is your account identifier on the platform. |

### Python SDK Reference Implementation

```python
# From lighter/signer_client.py
def create_auth_token_with_expiry(
    self,
    deadline: int = DEFAULT_10_MIN_AUTH_EXPIRY,  # 600 seconds
    *,
    timestamp: int = None,
    api_key_index: int = DEFAULT_API_KEY_INDEX
):
    if deadline == SignerClient.DEFAULT_10_MIN_AUTH_EXPIRY:
        deadline = 10 * SignerClient.MINUTE  # 600 seconds
    if timestamp is None:
        timestamp = int(time.time())

    # The actual deadline passed to native is: deadline + timestamp
    result = self.signer.CreateAuthToken(
        deadline + timestamp,  # Absolute expiration time
        api_key_index,
        self.account_index
    )

    auth = result.str.decode("utf-8") if result.str else None
    error = result.err.decode("utf-8") if result.err else None
    return auth, error
```

### C# Implementation Required

The current C# `SignerClient` does NOT implement `CreateAuthToken`. You need to add:

1. **P/Invoke declaration** in `NativeMethods.cs`:

```csharp
/// <summary>
/// Creates an authentication token for API read operations.
/// </summary>
/// <param name="deadline">Absolute expiration timestamp (Unix seconds = now + validity_period).</param>
/// <param name="apiKeyIndex">API key index used for signing.</param>
/// <param name="accountIndex">Account index.</param>
/// <returns>StrOrErr containing auth token or error.</returns>
[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal static extern StrOrErr CreateAuthToken(
    long deadline,
    int apiKeyIndex,
    long accountIndex);
```

2. **High-level method** in `SignerClient.cs`:

```csharp
/// <summary>
/// Creates an authentication token for API read operations.
/// The token is valid for the specified duration from now.
/// </summary>
/// <param name="validitySeconds">Token validity period in seconds. Default is 600 (10 minutes).</param>
/// <returns>Tuple containing (authToken, error). If error is not null, token creation failed.</returns>
public async Task<(string? authToken, string? error)> CreateAuthTokenAsync(int validitySeconds = 600)
{
    if (!_isInitialized)
        return (null, "Client not initialized. Call InitializeAsync first.");

    return await Task.Run(() =>
    {
        var deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + validitySeconds;
        var result = NativeMethods.CreateAuthToken(deadline, ApiKeyIndex, AccountIndex);
        return ProcessStrOrErr(result);
    });
}
```

## Authenticated Request Examples

### Option 1: Using `auth` Query Parameter

```
GET /accountActiveOrders?account_index=123&market_id=0&auth={auth_token}
```

**Full URL Example:**
```
https://mainnet.zklighter.elliot.ai/accountActiveOrders?account_index=123&market_id=0&auth=eyJhbGciOiJFZERTQSIsInR5cCI6IkpXVCJ9...
```

### Option 2: Using `Authorization` Header

```http
GET /accountActiveOrders?account_index=123&market_id=0 HTTP/1.1
Host: mainnet.zklighter.elliot.ai
Authorization: eyJhbGciOiJFZERTQSIsInR5cCI6IkpXVCJ9...
```

## Required Changes to LighterQueryClient

The `GetActiveOrdersAsync` method needs to accept and pass authentication:

### Interface Update (ILighterQueryClient.cs)

```csharp
/// <summary>
/// Gets active orders for an account on a specific market.
/// Requires authentication via auth token.
/// </summary>
/// <param name="accountIndex">Account index.</param>
/// <param name="marketId">Market ID (required by Lighter API).</param>
/// <param name="authToken">Authentication token from SignerClient.CreateAuthTokenAsync().</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>List of active orders for the specified market.</returns>
Task<List<Order>> GetActiveOrdersAsync(
    long accountIndex,
    int marketId,
    string authToken,
    CancellationToken cancellationToken = default);
```

### Implementation Update (LighterQueryClient.cs)

```csharp
public async Task<List<Order>> GetActiveOrdersAsync(
    long accountIndex,
    int marketId,
    string authToken,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(authToken))
        throw new ArgumentException("Auth token is required for accountActiveOrders", nameof(authToken));

    // Option 1: Pass auth as query parameter
    var response = await GetAsync<ActiveOrdersResponse>(
        $"accountActiveOrders?account_index={accountIndex}&market_id={marketId}&auth={Uri.EscapeDataString(authToken)}",
        cancellationToken);

    if (!response.IsSuccess)
        throw new LighterApiException(response.Message ?? "Failed to get active orders", response.Code);

    return response.Orders;
}
```

**Alternative: Using Authorization Header**

If you prefer using the header (cleaner URLs, but requires modifying the HTTP client):

```csharp
public async Task<List<Order>> GetActiveOrdersAsync(
    long accountIndex,
    int marketId,
    string authToken,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(authToken))
        throw new ArgumentException("Auth token is required for accountActiveOrders", nameof(authToken));

    var request = new HttpRequestMessage(
        HttpMethod.Get,
        $"accountActiveOrders?account_index={accountIndex}&market_id={marketId}");
    request.Headers.Add("Authorization", authToken);

    var response = await _httpClient.SendAsync(request, cancellationToken);
    var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
    await EnsureSuccessStatusCodeAsync(response);

    var result = JsonSerializer.Deserialize<ActiveOrdersResponse>(rawJson, _jsonOptions);
    if (result == null || !result.IsSuccess)
        throw new LighterApiException(result?.Message ?? "Failed to get active orders", result?.Code ?? -1);

    return result.Orders;
}
```

## Usage Example (Complete Flow)

```csharp
// 1. Initialize signer client (done once at startup)
var signerClient = new SignerClient();
await signerClient.InitializeAsync(
    url: "https://mainnet.zklighter.elliot.ai",
    privateKey: "your-private-key",
    chainId: 304,  // Mainnet
    apiKeyIndex: 0,
    accountIndex: 123
);

// 2. Create auth token before each read operation (or cache and refresh)
var (authToken, authError) = await signerClient.CreateAuthTokenAsync(validitySeconds: 600);
if (authError != null)
    throw new Exception($"Failed to create auth token: {authError}");

// 3. Make authenticated request
var queryClient = new LighterQueryClient(httpClient);
var activeOrders = await queryClient.GetActiveOrdersAsync(
    accountIndex: 123,
    marketId: 0,
    authToken: authToken!
);
```

## Token Caching Recommendations

Since auth tokens are valid for 10 minutes (600 seconds), implement caching:

```csharp
public class AuthTokenManager
{
    private readonly SignerClient _signerClient;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Refresh 30 seconds before expiry to avoid race conditions
    private const int RefreshBufferSeconds = 30;
    private const int TokenValiditySeconds = 600;

    public async Task<string> GetValidAuthTokenAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _cachedToken;

            var (token, error) = await _signerClient.CreateAuthTokenAsync(TokenValiditySeconds);
            if (error != null)
                throw new Exception($"Failed to create auth token: {error}");

            _cachedToken = token!;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(TokenValiditySeconds - RefreshBufferSeconds);

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

## Error Codes Reference

| Code | Message | Cause | Solution |
|------|---------|-------|----------|
| 20001 | auth query param and Authorization header are both empty | No authentication provided | Add `auth` query param OR `Authorization` header |
| 20001 | invalid param: auth | Invalid or expired auth token | Generate fresh token with `CreateAuthToken` |
| 20001 | field "market_id" is not set | Missing required `market_id` param | Add `market_id` to query string |

## Implementation Priority

**CRITICAL** - This is a blocking issue. The bot cannot retrieve active orders without authentication.

### Required Steps:
1. Add `CreateAuthToken` P/Invoke to `NativeMethods.cs`
2. Add `CreateAuthTokenAsync` method to `SignerClient.cs`
3. Update `ILighterQueryClient.GetActiveOrdersAsync` signature to include `authToken`
4. Update `LighterQueryClient.GetActiveOrdersAsync` to pass auth token
5. Update all callers to generate and pass auth tokens

### Optional but Recommended:
1. Implement `AuthTokenManager` for token caching
2. Consider adding auth to other private endpoints if needed

## Sources

- [Lighter Python SDK - signer_client.py](https://github.com/elliottech/lighter-python/blob/main/lighter/signer_client.py)
- [Lighter Python SDK - order_api.py](https://github.com/elliottech/lighter-python/blob/main/lighter/api/order_api.py)
- [Lighter API Documentation](https://apidocs.lighter.xyz/reference/accountactiveorders)
- [Lighter Signer Header - Windows](https://github.com/elliottech/lighter-python/blob/main/lighter/signers/lighter-signer-windows-amd64.h)
