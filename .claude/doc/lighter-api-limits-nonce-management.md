# Lighter DEX API: Limits, Batch Transactions & Nonce Management

## Executive Summary

This document details the API limits, batch transaction constraints, and nonce error handling for the Lighter DEX API, based on official documentation research conducted 2025-12-11.

---

## 1. Maximum Open Orders Per Account

**Status: NOT EXPLICITLY DOCUMENTED**

The Lighter DEX official documentation does NOT specify a hard limit on the maximum number of open orders per account. Key observations:

- The Order Book Tree stores all active and pending orders associated with an account
- The Account Order Tree indexing encodes price and nonce into leaf indices, supporting logarithmic operations
- Each limit order consumes **Order Margin** - which is a checking mechanism used when placing new orders
- Practical limit is constrained by:
  1. Available margin (Order Margin system)
  2. Rate limits on order creation
  3. Volume Quota allowance

**Recommendation**: Treat the practical limit as being constrained by your margin and rate limits rather than a fixed order count.

---

## 2. Batch Transaction Limits

### Maximum Operations Per Batch

The implementation in `LighterWebSocketClient.cs` enforces:

```csharp
if (txTypes.Length > 50)
    throw new ArgumentException("Maximum 50 transactions per batch");
```

**Source**: Line 1099-1100 in `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\LighterWebSocketClient.cs`

### Batch-Related Rate Limits

| Endpoint | Weight | Notes |
|----------|--------|-------|
| `sendTx` | 6 per user | Single transaction |
| `sendTxBatch` | 6 per user | Multiple transactions in one request |
| `nextNonce` | 6 per user | Nonce synchronization |

### Volume Quota System

Volume Quota is a separate system from rate limits:

| Aspect | Value |
|--------|-------|
| Initial quota | 1,000 transactions |
| Maximum stackable | 5,000,000 transactions |
| Quota accumulation | $10 trading volume = 1 additional TX allowance |
| Free allowance | 1 `SendTx`/`SendTxBatch` per 15 seconds |
| Expiration | Never expires |

**Reference**: [Volume Quota Program](https://apidocs.lighter.xyz/docs/volume-quota-program)

### Gas-Based Limits

From the Lighter Python SDK: The number of operations in a batch is limited to **4 million gas**.

---

## 3. Error Code 21104: Invalid Nonce

### Error Details

| Attribute | Value |
|-----------|-------|
| Error Code | `21104` |
| Error Name | `invalid nonce` |
| Category | Transaction signing/submission error |

### Root Cause Analysis

The `21104` error occurs when:

1. **Nonce too low**: The nonce sent is lower than or equal to a previously used nonce
2. **Nonce too high**: The nonce is significantly higher than expected (gap detection)
3. **Stale nonce**: Multiple clients using the same API key without coordination
4. **Server lag**: Nonce not yet updated on server after recent transaction

### How Nonces Work in Lighter

```
Nonce Flow:
1. Each account has an independent nonce counter per API_KEY_INDEX
2. Nonces must be strictly increasing (monotonic)
3. Server returns: nextNonce = N (use nonce N for next transaction)
4. Client increments locally after each transaction
5. Server-side nonce updates are NOT instantaneous
```

### Nonce Management Best Practices

From the official documentation ([Nonce Management](https://apidocs.lighter.xyz/docs/nonce-management)):

1. **Use multiple API keys**: You can utilize up to **255 different `API_KEY_INDEX` values** (indices 2-254), each managing its own independent nonce stream

2. **Local tracking with server sync**:
   - Track nonce locally and increment after non-error responses
   - Sync from server on startup or after errors
   - Note: Server-side updates don't occur instantaneously

3. **Parallel processing**: Using multiple API keys allows concurrent transaction submission without nonce conflicts

### Current Implementation Fix (Session 1 Context)

Two bugs were fixed in the GridBot implementation:

#### Bug 1: Off-by-one error in `SyncNonceAsync`

```csharp
// OLD (wrong): Server returns 500, set _currentNonce=500, GetNextNonce returns 501
SetNonce(nonceResponse.Nonce);

// NEW (fixed): Server returns 500, set _currentNonce=499, GetNextNonce returns 500
SetNonce(nonceResponse.Nonce - 1);
```

**Reason**: `GetNextNonce()` uses pre-increment (`++_currentNonce`), so `_currentNonce` must be set to `serverNonce - 1`.

#### Bug 2: No automatic nonce sync at startup

Added `SyncNonceFromServerAsync()` call during `InitializeAsync()` when `InitialNonce == 0`.

### Retry Mechanism

The current implementation (`WsLighterCommandClient.cs`) handles nonce errors:

```csharp
private const int InvalidNonceErrorCode = 21104;
private const int MaxNonceRetries = 3;
private const int NonceRetryDelayMs = 100;

// ExecuteWithNonceRetryAsync:
// 1. Catches LighterApiException with code 21104
// 2. Syncs nonce from server via REST API
// 3. Retries up to 3 times with 100ms delay
```

---

## 4. Rate Limits Summary

### REST API Limits

| Account Type | Request Limit |
|--------------|---------------|
| Premium | 24,000 weighted requests per minute |
| Standard | 60 requests per minute |

### High-Weight Endpoints

| Endpoint | Weight |
|----------|--------|
| `sendTx`, `sendTxBatch`, `nextNonce` | 6 |
| `/trades`, `/recentTrades` | 1,200 |

### WebSocket Limits

| Limit Type | Value |
|------------|-------|
| Connections per IP | 100 |
| Subscriptions per connection | 100 |
| Messages per minute | 200 |
| Max inflight messages | 50 |

### Standard Account Transaction Limits

| Operation | Limit |
|-----------|-------|
| Withdrawals | 2 per minute |
| Leverage updates | 1 per minute |
| Transfers | 1 per minute |

### Error Response

Exceeding rate limits returns:
- REST: HTTP `429 Too Many Requests`
- WebSocket: Potential disconnection

**Reference**: [Rate Limits](https://apidocs.lighter.xyz/docs/rate-limits)

---

## 5. API Keys Information

| Aspect | Value |
|--------|-------|
| Max API keys per account | 256 |
| Usable key indices | 2-254 (253 keys) |
| Nonce tracking | Independent per API_KEY_INDEX |

**Reference**: [Sub-Accounts and API Keys](https://docs.lighter.xyz/perpetual-futures/sub-accounts-and-api-keys)

---

## 6. Account Types

| Feature | Standard (Default) | Premium (Opt-in) |
|---------|-------------------|------------------|
| Maker fee | 0% | 0.002% |
| Taker fee | 0% | 0.02% |
| Maker latency | 200ms | 0ms |
| Cancel latency | 100ms | 0ms |
| Taker latency | 300ms | 150ms |
| Volume quota | Not required | Required |

**Reference**: [Account Types](https://apidocs.lighter.xyz/docs/account-types)

---

## 7. Implementation Recommendations

### For Grid Bot Trading

1. **Batch Size**: Keep batches under 50 operations (hard limit in implementation)
2. **Grid Orders**: With 10 orders per side (MaxOrdersPerSide = 10), total of 20 orders fits well within batch limits
3. **Nonce Safety**: Always sync nonce on startup and implement retry logic for 21104 errors

### For High-Frequency Trading

1. Use multiple API keys for parallel order streams
2. Consider Premium account for zero maker/cancel latency
3. Build volume to accumulate quota for burst capacity

### Error Handling Pattern

```csharp
// Recommended pattern for handling nonce errors
try
{
    return await CreateOrderAsync(request, ct);
}
catch (LighterApiException ex) when (ex.Code == 21104)
{
    await SyncNonceAsync(accountIndex, apiKeyIndex, ct);
    return await CreateOrderAsync(request, ct); // Retry once
}
```

---

## Sources

- [Nonce Management Documentation](https://apidocs.lighter.xyz/docs/nonce-management)
- [Rate Limits Documentation](https://apidocs.lighter.xyz/docs/rate-limits)
- [Volume Quota Program](https://apidocs.lighter.xyz/docs/volume-quota-program)
- [Account Types](https://apidocs.lighter.xyz/docs/account-types)
- [Sub-Accounts and API Keys](https://docs.lighter.xyz/perpetual-futures/sub-accounts-and-api-keys)
- [Get Started for Programmers](https://apidocs.lighter.xyz/docs/get-started-for-programmers-1)
- [Lighter Python SDK (GitHub)](https://github.com/elliottech/lighter-python)
- GridBot implementation: `WsLighterCommandClient.cs`, `LighterWebSocketClient.cs`
- Session context: `.claude/sessions/context_session_1.md`
