# Lighter DEX Rate Limiting Research

**Date:** 2025-12-07
**Status:** Research Complete
**Sources:**
- https://apidocs.lighter.xyz/docs/rate-limits
- https://apidocs.lighter.xyz/docs/volume-quota-program
- https://apidocs.lighter.xyz/docs/account-types

---

## Executive Summary

Lighter DEX enforces rate limits based on **both IP address AND L1 wallet address**. The limits are dramatically different between Standard (default) and Premium accounts - 60 vs 24,000 weighted requests per minute for REST APIs.

---

## 1. How Are Rate Limits Counted?

### Rate Limit Scope

Rate limits apply to **BOTH**:
- **IP Address** - All requests from the same IP share a limit
- **L1 Wallet Address** - All requests from the same Ethereum wallet share a limit

**Important:** Sub-accounts share the same L1 wallet address, so they share rate limits with the main account.

### NOT counted separately:
- API keys (256 keys allowed per account, but they share the wallet's limit)
- Sub-accounts (share main account's L1 wallet limit)

---

## 2. Rate Limit Thresholds

### REST API - Main Endpoint (mainnet.zklighter.elliot.ai)

| Account Type | Limit | Window |
|-------------|-------|--------|
| **Premium** | 24,000 weighted requests | Rolling 60 seconds |
| **Standard** | 60 weighted requests | Rolling 60 seconds |

**Note:** This is a 400x difference between account types!

### Explorer API (explorer.elliot.ai)

| Account Type | Limit | Window |
|-------------|-------|--------|
| Both | 15 weighted requests | Rolling 60 seconds |

### WebSocket Limits (Per IP)

| Limit Type | Value |
|-----------|-------|
| Maximum connections | 100 |
| Subscriptions per connection | 100 |
| Total subscriptions | 1,000 |
| Messages per minute | 200 |
| Inflight messages maximum | 50 |
| Unique accounts maximum | 10 |

---

## 3. Endpoint Weight Classifications

### Low Weight (Efficient for High-Frequency Use)

| Endpoint | Weight | Notes |
|----------|--------|-------|
| `/api/v1/sendTx` | 6 | Order submission |
| `/api/v1/sendTxBatch` | 6 | Batch order submission |
| `/api/v1/nextNonce` | 6 | Nonce synchronization |

### Medium Weight

| Endpoint | Weight | Notes |
|----------|--------|-------|
| `/api/v1/publicPools` | 50 | Pool information |
| `/api/v1/txFromL1TxHash` | 50 | Transaction lookup |
| `/api/v1/accountInactiveOrders` | 100 | Historical orders |
| `/api/v1/deposit/latest` | 100 | Deposit status |
| `/api/v1/apikeys` | 150 | API key management |
| All other endpoints | 300 | Default weight |

### High Weight (Use Sparingly)

| Endpoint | Weight | Notes |
|----------|--------|-------|
| `/api/v1/trades` | 1,200 | Trade history |
| `/api/v1/recentTrades` | 1,200 | Recent trades |

### Explorer API Weights

| Endpoint | Weight |
|----------|--------|
| `/api/search` | 3 |
| `/api/accounts/param/positions` | 2 |
| `/api/accounts/param/logs` | 2 |
| All other explorer endpoints | 1 |

---

## 4. Transaction-Specific Limits (Standard Accounts Only)

Standard accounts have additional per-transaction-type limits:

| Transaction Type | Limit |
|-----------------|-------|
| Default transactions | 40/minute |
| `L2Withdraw` | 2/minute |
| `L2UpdateLeverage` | 1/minute |
| `L2CreateSubAccount` | 2/minute |
| `L2CreatePublicPool` | 2/minute |
| `L2ChangePubKey` | 2 per 10 seconds |
| `L2Transfer` | 1/minute |

---

## 5. HTTP 429 Behavior

### When Rate Limit Is Exceeded

- **HTTP Status:** `429 Too Many Requests`
- **WebSocket:** Connection may be disconnected for excessive messages
- **Retry-After Header:** Not explicitly documented in Lighter docs

### Recommended Recovery Strategy

1. **Check for Retry-After header** - If present, wait that duration
2. **Implement exponential backoff:**
   - Initial wait: 1 second
   - Double wait time after each retry: 2s, 4s, 8s, 16s...
   - Maximum wait: 60 seconds
3. **Add jitter** - Random 0-500ms to avoid thundering herd
4. **Track remaining capacity** - If available in response headers

---

## 6. Volume Quota Program (Premium Accounts Only)

### How It Works

In addition to rate limits, Premium accounts have a **Volume Quota** system:

| Aspect | Details |
|--------|---------|
| Earning rate | +1 quota per $10 trading volume |
| Initial quota (new accounts) | 1,000 |
| Maximum stackable quota | 5,000,000 |
| Quota expiry | Never expires |
| Free transactions | 1 free SendTx/SendTxBatch every 15 seconds |

### Volume Quota vs Rate Limits

These are **independent systems**:
- Rate Limits: Maximum 24,000 weight per rolling minute
- Volume Quota: Total transaction allowance based on trading volume

You must comply with **both** - hitting either limit blocks transactions.

### Sub-Account Sharing

Volume quota is **shared across all sub-accounts** under the same L1 address.

---

## 7. Account Types Comparison

| Feature | Standard | Premium |
|---------|----------|---------|
| Target User | Retail traders | HFT traders |
| Maker Fee | 0% | 0.002% |
| Taker Fee | 0% | 0.02% |
| Maker Latency | 200ms | 0ms |
| Taker Latency | 300ms | 150ms |
| Cancel Latency | 100ms | 0ms |
| REST API Limit | 60 weight/min | 24,000 weight/min |
| Volume Quota | No | Yes |
| Switching | Via `/changeAccountTier` endpoint |

### Switching Requirements
- No open positions
- No open orders
- 24-hour minimum wait between switches

---

## 8. Best Practices for Trading Bot

### For Premium Accounts (Recommended for Bots)

1. **Batch orders when possible**
   - `sendTxBatch` has same weight (6) as single `sendTx`
   - Batch multiple orders into single request

2. **Use WebSocket for market data**
   - Avoid polling REST endpoints
   - WebSocket has separate limits (200 messages/min)

3. **Cache static data**
   - Order books metadata rarely changes
   - Funding rates update on schedule

4. **Monitor endpoint weights**
   - `recentTrades` = 1,200 weight (use sparingly!)
   - `sendTx` = 6 weight (very efficient)

5. **Calculate capacity before requests**
   ```
   Premium budget: 24,000 weight / 60 seconds = 400 weight/second

   Example capacity per second:
   - 66 order submissions (400/6)
   - OR 1 recent trades query (400/300 for most endpoints)
   ```

### For Standard Accounts (Not Recommended for Active Trading)

With only 60 weight/minute:
- Can only do ~10 order submissions/minute (60/6)
- OR only 1 "recentTrades" call every 20 minutes

**Recommendation:** Upgrade to Premium for any serious trading bot.

### Rate Limit Recovery

```csharp
// Recommended backoff pattern
public async Task<T> ExecuteWithBackoff<T>(Func<Task<T>> operation)
{
    int retryCount = 0;
    int baseDelayMs = 1000;

    while (true)
    {
        try
        {
            return await operation();
        }
        catch (LighterApiException ex) when (ex.Code == 429)
        {
            if (retryCount >= 5) throw;

            var delay = baseDelayMs * Math.Pow(2, retryCount);
            var jitter = Random.Shared.Next(0, 500);

            await Task.Delay((int)delay + jitter);
            retryCount++;
        }
    }
}
```

---

## 9. Current Implementation Gaps

Reviewing the existing `LighterCommandClient.cs` and `LighterQueryClient.cs`:

### Missing Rate Limit Handling
- No 429 detection or retry logic
- No rate limit tracking/budgeting
- No exponential backoff implementation

### Existing Error Handling
- Nonce retry logic exists (error code 21104)
- HTTP status code captured in exceptions
- No specific 429 handling

### Recommendations for Implementation
1. Add HTTP 429 detection in `EnsureSuccessStatusCodeAsync`
2. Implement exponential backoff with jitter
3. Consider adding rate limit budget tracking
4. Add response header parsing for rate limit info (if provided)

---

## 10. Summary Table

| Question | Answer |
|----------|--------|
| Per account? | Yes (L1 wallet address) |
| Per subaccount? | No - shares main account limit |
| Per API key? | No - shares wallet limit |
| Per IP address? | Yes (additional limit) |
| Standard API limit | 60 weight/min |
| Premium API limit | 24,000 weight/min |
| 429 response | Yes, implement backoff |
| Retry-After header | Not documented |

---

## References

- [Rate Limits Documentation](https://apidocs.lighter.xyz/docs/rate-limits)
- [Volume Quota Program](https://apidocs.lighter.xyz/docs/volume-quota-program)
- [Account Types](https://apidocs.lighter.xyz/docs/account-types)
- [Sub-Accounts and API Keys](https://docs.lighter.xyz/perpetual-futures/sub-accounts-and-api-keys)
