# Extended DEX Integration - Risk Management Framework

## Executive Summary

This document defines the risk management rules, thresholds, and edge case handling for integrating the Extended DEX (Starknet-based perpetual futures exchange) into GridBot. The framework addresses the unique characteristics of Starknet-based exchanges, including asynchronous order processing, SNIP12 signatures, and rate limiting.

---

## 1. Asynchronous Order Rejection Handling

### Risk Category: Order State Uncertainty

Extended processes orders asynchronously - an order accepted by the API may still be rejected by the matching engine. This creates a critical state management challenge.

### Thresholds
- **Order Confirmation Timeout**: 30 seconds (Maximum time to wait for WebSocket confirmation)
- **Pending Order Limit**: 50 per market (Maximum unconfirmed orders before blocking new submissions)
- **Orphan Order Age**: 60 seconds (Time after which an unconfirmed order is considered potentially orphaned)

### Rules

1. **IF** order API response returns HTTP 200/201 **THEN** mark order as `PendingConfirmation` (NOT `Active`)

2. **IF** WebSocket receives order confirmation with matching `id` **THEN** transition order to `Active` state

3. **IF** WebSocket receives order rejection for `id` **THEN** transition order to `Rejected`, log reason, trigger inventory recalculation

4. **IF** order remains in `PendingConfirmation` state > 30 seconds **THEN** query `/user/orders` to verify actual state, update local state accordingly

5. **IF** pending order count per market >= 50 **THEN** block new order submissions until confirmations reduce count below 40

6. **IF** WebSocket disconnects while orders are `PendingConfirmation` **THEN** immediately query `/user/orders` on reconnect to reconcile all pending orders

### Edge Cases

- **Scenario**: WebSocket message arrives before HTTP response completes
  **Response**: Use order `id` (client-generated) as correlation key. Accept confirmation regardless of HTTP response timing. If HTTP fails but WS confirms, log warning but consider order active.

- **Scenario**: Order confirmed on WS but HTTP returns 4xx/5xx error
  **Response**: Trust WebSocket confirmation. Log discrepancy for investigation. Order is considered active.

- **Scenario**: Network partition causes missed rejection message
  **Response**: Periodic reconciliation (every 60 seconds) compares local state with `/user/orders`. Any local `Active` order not found on exchange is marked `Unknown` and excluded from inventory calculations.

- **Scenario**: Order partially filled then rejected
  **Response**: Rejection message should include fill information. Update position based on actual fills. Trigger immediate position reconciliation via `/user/positions`.

### Priority Level: Critical

---

## 2. Stark Signature Nonce Management

### Risk Category: Transaction Integrity

Starknet nonces must be sequential (>=1, <=2^31). Nonce mismatches cause transaction rejection, potentially leaving the system in an inconsistent state.

### Thresholds
- **Maximum Nonce Value**: 2,147,483,647 (2^31 - 1)
- **Nonce Sync Retry Attempts**: 5
- **Nonce Sync Retry Delay**: 100ms base, exponential backoff up to 2 seconds
- **Nonce Reset Warning Threshold**: 2,000,000,000 (Warn when approaching limit)

### Rules

1. **IF** operation fails with nonce error code **THEN** sync nonce from `/user/account/info`, retry operation up to 5 times with exponential backoff

2. **IF** nonce sync retry count >= 5 **THEN** halt all order operations, raise critical alert, require manual intervention

3. **IF** current nonce > 2,000,000,000 **THEN** log warning about approaching nonce limit, alert operations team

4. **IF** current nonce >= 2,147,483,647 **THEN** halt all operations, this account cannot submit more transactions

5. **IF** multiple concurrent operations attempt nonce increment **THEN** serialize all signing operations through a single-threaded nonce manager (lock-based)

6. **IF** signing operation fails after nonce increment **THEN** DO NOT decrement nonce (nonce was consumed), next operation must use next sequential nonce

### Edge Cases

- **Scenario**: Server returns nonce lower than local nonce
  **Response**: This indicates server state rollback or data corruption. Log critical error, halt operations, alert for manual investigation. DO NOT automatically adjust to lower nonce.

- **Scenario**: Concurrent batch order submission consumes multiple nonces, one fails
  **Response**: All subsequent orders in batch will fail (nonce gap). Resync nonce from server. Re-sign and resubmit remaining orders with new nonces.

- **Scenario**: Application crash after nonce increment but before submission
  **Response**: On startup, always sync nonce from server before first operation. The "lost" nonce is acceptable - gaps are allowed, but lower values are rejected.

- **Scenario**: Server nonce endpoint returns error during sync
  **Response**: Retry with exponential backoff. If all retries fail, halt order operations until nonce can be synchronized. Read-only operations may continue.

- **Scenario**: Two application instances using same API key
  **Response**: This is an architecture violation. Detect via unexpected nonce errors. Log critical warning. Second instance should fail fast and not compete for nonces.

### Priority Level: Critical

---

## 3. Rate Limiting Strategy

### Risk Category: API Availability

Extended enforces rate limits of 1,000 requests/minute (standard) or 60,000 requests/5 minutes (market makers). Exceeding limits returns HTTP 429.

### Thresholds
- **Standard Rate Limit**: 1,000 requests/minute (16.67 req/sec)
- **Market Maker Rate Limit**: 60,000 requests/5 minutes (200 req/sec)
- **Safety Margin**: 80% of limit (use 800/min or 48,000/5min as soft ceiling)
- **Rate Limit Backoff Base**: 1 second
- **Rate Limit Backoff Maximum**: 60 seconds
- **Burst Allowance**: 20 requests in 1 second (for batch operations)

### Rules

1. **IF** requests in current window >= 80% of limit **THEN** begin request throttling (add 50ms delay between requests)

2. **IF** requests in current window >= 95% of limit **THEN** queue non-critical requests, only allow critical requests (cancellations, position queries)

3. **IF** HTTP 429 received **THEN** immediately pause all requests for 1 second, then resume with exponential backoff (1s, 2s, 4s, up to 60s)

4. **IF** 3 consecutive HTTP 429 responses **THEN** pause all requests for 60 seconds, log warning, reduce request rate by 50% for next 5 minutes

5. **IF** rate limit bucket type is market maker **THEN** use 5-minute sliding window for tracking (vs 1-minute for standard)

6. **IF** WebSocket connection is healthy **THEN** prefer WebSocket for data that can be streamed (orderbook, positions, trades) to reduce REST API load

### Request Priority Classification

| Priority | Request Type | Behavior Under Limit |
|----------|--------------|---------------------|
| Critical | Cancel Order, Cancel All, Get Positions | Always allowed, bypass soft limit |
| High | Create Order, Modify Order | Allowed up to 95% limit |
| Medium | Get Orders, Get Balance | Throttled at 80% limit |
| Low | Get Markets, Get Orderbook, Get Candles | Deferred when above 70% limit |

### Edge Cases

- **Scenario**: Rate limit headers missing from response
  **Response**: Assume conservative rate limit (standard tier). Track requests locally using sliding window.

- **Scenario**: Multiple order cancellations needed under rate limit pressure
  **Response**: Use mass cancel endpoint (`/user/order/massCancel`) to cancel multiple orders in single request.

- **Scenario**: Need to submit urgent order but at rate limit
  **Response**: Critical operations bypass soft limit. If hard limit (429) is hit, wait minimum backoff then retry critical operation first.

- **Scenario**: Clock skew between client and server affects window calculation
  **Response**: Use server timestamps from response headers if available. Add 5% safety margin to compensate for potential skew.

### Priority Level: High

---

## 4. Order Validation and Safety Checks

### Risk Category: Order Integrity

Pre-submission validation prevents invalid orders from consuming nonces and API rate limit.

### Thresholds
- **Maximum Order Expiry (Mainnet)**: 90 days (7,776,000 seconds)
- **Maximum Order Expiry (Testnet)**: 28 days (2,419,200 seconds)
- **Minimum Order Size**: Market-specific (from `/info/markets`)
- **Maximum Order Size**: Market-specific (from `/info/markets`)
- **Price Tick Size**: Market-specific (from `/info/markets`)
- **Size Step Size**: Market-specific (from `/info/markets`)
- **Maximum Leverage**: Market-specific (from `/info/markets`)
- **Fee Precision**: 4 decimal places (0.0001 = 0.01%)

### Rules

1. **IF** order quantity < market minimum size **THEN** reject order locally, do not submit to API

2. **IF** order quantity > market maximum size **THEN** reject order locally, log warning about size limit

3. **IF** order price not aligned to tick size **THEN** round to nearest valid tick (toward less aggressive price)

4. **IF** order size not aligned to step size **THEN** round down to nearest valid step

5. **IF** order expiry > maximum expiry for environment **THEN** cap expiry at maximum, log info message

6. **IF** order expiry <= 0 **THEN** reject order locally (must be positive epoch milliseconds)

7. **IF** leverage requested > market maximum leverage **THEN** reject order, log error

8. **IF** order would exceed position limit **THEN** reject order locally, return specific error message

9. **IF** order side is SELL and no existing position **THEN** validate margin availability for new short position

10. **IF** reduce-only order would increase position **THEN** reject order locally

### Edge Cases

- **Scenario**: Market configuration changes between validation and submission
  **Response**: Accept potential rejection from server. Log discrepancy. Refresh market config cache.

- **Scenario**: Order passes local validation but fails server validation
  **Response**: Do NOT retry automatically (nonce was consumed). Log validation failure reason. Return error to caller.

- **Scenario**: Fee parameter exceeds expected maximum
  **Response**: If fee > 0.01 (1%), log warning but allow submission. Exchange fee schedules may change.

- **Scenario**: Client-provided order ID conflicts with existing order
  **Response**: Generate new unique ID locally. Log original ID for reference.

### Priority Level: High

---

## 5. WebSocket Connection Resilience

### Risk Category: Data Freshness

WebSocket provides real-time order updates. Connection loss creates state uncertainty.

### Thresholds
- **Heartbeat Interval**: 30 seconds (ping/pong)
- **Heartbeat Timeout**: 10 seconds (consider disconnected if no pong)
- **Reconnection Delay Base**: 1 second
- **Reconnection Delay Maximum**: 60 seconds
- **Maximum Reconnection Attempts**: Unlimited (with backoff)
- **Data Staleness Threshold**: 5 seconds (halt new orders if no data for 5s)
- **Disconnection Count Alert**: 5 in 5 minutes (indicates connectivity issues)

### Rules

1. **IF** no heartbeat pong received within 10 seconds **THEN** consider connection dead, initiate reconnection

2. **IF** WebSocket disconnects unexpectedly **THEN** immediately query REST API to reconcile all open orders and positions

3. **IF** WebSocket reconnection fails 3 times in 1 minute **THEN** halt new order submissions, continue position monitoring via REST

4. **IF** DataAge > 5 seconds **THEN** mark connection as `Stale`, halt new order submissions until fresh data received

5. **IF** DisconnectCount24h > 20 **THEN** log warning, consider switching to REST-only mode for stability

6. **IF** WebSocket message parsing fails **THEN** log error with raw message, do NOT disconnect (isolated message failure)

7. **IF** subscription to private channel fails **THEN** retry with fresh auth token (tokens may expire)

### Edge Cases

- **Scenario**: WebSocket connected but no messages received
  **Response**: Use heartbeat mechanism. If no heartbeat response, treat as disconnected.

- **Scenario**: Partial message received (connection drops mid-message)
  **Response**: Discard partial message. Buffer messages until complete JSON received.

- **Scenario**: Server closes connection with specific error code
  **Response**: Parse close reason. If auth failure, refresh token and reconnect. If rate limit, apply backoff. If server error, reconnect with standard backoff.

- **Scenario**: WebSocket and REST API return conflicting order state
  **Response**: Trust the most recent data by timestamp. If timestamps equal, prefer WebSocket (likely more current). Log discrepancy.

### Priority Level: Critical

---

## 6. Settlement and Position Safety

### Risk Category: Capital Protection

Extended is trustless - assets remain in Starknet smart contracts. Position and balance verification is critical.

### Thresholds
- **Position Reconciliation Interval**: 60 seconds (verify positions match local state)
- **Balance Reconciliation Interval**: 60 seconds (verify available margin)
- **Maximum Unrealized Loss**: Configurable per strategy (suggest 5% of portfolio)
- **Liquidation Warning Threshold**: 20% above maintenance margin
- **Emergency Reduce Threshold**: 10% above maintenance margin

### Rules

1. **IF** unrealized PnL < -5% of portfolio value **THEN** halt new position-increasing orders, allow reduces only

2. **IF** margin ratio < 120% of maintenance margin **THEN** begin reducing positions, cancel non-reduce orders

3. **IF** margin ratio < 110% of maintenance margin **THEN** emergency close 50% of largest positions at market

4. **IF** position size from REST differs from local tracking by > 1% **THEN** force resync, log warning, verify no missed fills

5. **IF** available balance < 0 after proposed order **THEN** reject order before submission

6. **IF** funding rate > 0.1% (for long) or < -0.1% (for short) **THEN** log warning, consider position reduction to avoid funding drag

### Edge Cases

- **Scenario**: Position shows on REST but not in WebSocket feed
  **Response**: Trust REST as source of truth for reconciliation. WebSocket may have missed update.

- **Scenario**: Liquidation occurs while system is offline
  **Response**: On startup, query positions and balances first. Accept liquidation as fact. Log event for post-mortem.

- **Scenario**: Funding payment significantly impacts margin
  **Response**: Monitor funding payments via WebSocket. Recalculate margin ratios after each funding event.

- **Scenario**: Market becomes restricted or delisted
  **Response**: Receive notification via WebSocket or error on order. Mark market as `Inactive`. Allow only close operations.

### Priority Level: Critical

---

## 7. Error Code Handling

### Error Response Matrix

| HTTP Code | Error Type | Retry | Action |
|-----------|------------|-------|--------|
| 400 | Bad Request | No | Log error, return failure to caller, review request format |
| 401 | Unauthorized | Yes (1x) | Refresh API key/token, retry once |
| 403 | Forbidden | No | Log error, operation not permitted for account |
| 404 | Not Found | No | Resource does not exist, log and proceed |
| 422 | Unprocessable | No | Validation failed, log specific reason, do not retry |
| 429 | Rate Limited | Yes | Apply rate limit backoff strategy |
| 500 | Server Error | Yes (3x) | Retry with exponential backoff |
| 502 | Bad Gateway | Yes (3x) | Retry with exponential backoff |
| 503 | Unavailable | Yes (3x) | Retry with exponential backoff, consider halting |
| 504 | Gateway Timeout | Yes (3x) | Retry with exponential backoff |

### Application-Level Error Codes (from response body)

Extended may return application-specific error codes in the response body. Define handling for known codes:

| Code Pattern | Meaning | Action |
|--------------|---------|--------|
| NONCE_* | Nonce related | Sync nonce, retry |
| ORDER_NOT_FOUND | Order does not exist | Remove from local state |
| INSUFFICIENT_MARGIN | Not enough margin | Reject, log, recalculate positions |
| MARKET_CLOSED | Market not trading | Queue order for market open or reject |
| POSITION_LIMIT | Max position reached | Reject, allow reduces only |
| RATE_LIMIT | Too many requests | Apply rate limit backoff |

### Priority Level: High

---

## 8. Integration Component Safety Requirements

### IOrderClient Implementation

```
Required Safety Behaviors:
1. Pre-validate all orders against cached market config
2. Serialize order signing through nonce manager
3. Track order state machine: Created -> PendingConfirmation -> Active/Rejected
4. Implement automatic nonce retry (up to 5 attempts)
5. Support graceful degradation when WebSocket unavailable (REST fallback)
```

### IAccountClient Implementation

```
Required Safety Behaviors:
1. Cache positions and balances with TTL (max 5 seconds)
2. Provide stale data indicator when cache expires without refresh
3. Reconcile WebSocket updates with periodic REST verification
4. Expose margin ratio calculations for risk module
```

### IRealtimeDataProvider Implementation

```
Required Safety Behaviors:
1. Implement automatic reconnection with exponential backoff
2. Track and expose DataAge for staleness detection
3. Buffer messages during brief disconnections (< 5 seconds)
4. Reconcile state via REST on reconnection
5. Expose DisconnectCount24h metric
```

### IAuthenticationProvider Implementation

```
Required Safety Behaviors:
1. Thread-safe nonce management with lock
2. Atomic nonce increment (no decrement on failure)
3. Nonce sync on startup before first operation
4. Warn when nonce approaches 2^31 limit
5. Token refresh before expiry (refresh at 80% of validity)
```

### IScalingProvider Implementation

```
Required Safety Behaviors:
1. Cache market scaling parameters
2. Validate decimal precision on all conversions
3. Round prices toward less aggressive (buy down, sell up)
4. Round sizes down (never exceed intended quantity)
5. Reject values that would result in zero after scaling
```

---

## 9. Startup and Shutdown Procedures

### Startup Sequence

1. Load configuration and validate all required settings
2. Initialize authentication provider, sync nonce from server
3. Establish WebSocket connection, subscribe to private channel
4. Wait for initial snapshot from WebSocket (timeout: 10 seconds)
5. Query REST API for current positions and open orders
6. Reconcile WebSocket state with REST state
7. Mark system as `Ready` only after successful reconciliation
8. Begin normal operation

### Shutdown Sequence

1. Mark system as `ShuttingDown`, reject new orders
2. Cancel all pending orders (optional, configurable)
3. Wait for pending order confirmations (timeout: 30 seconds)
4. Close WebSocket connection gracefully
5. Persist current state to recovery file
6. Exit

### Recovery After Crash

1. Load last known state from recovery file (if available)
2. Query REST API for current positions and orders
3. Compare with recovery file to identify discrepancies
4. Log any positions/orders that differ from expected
5. Use REST state as authoritative source
6. Resume normal startup sequence

---

## 10. Monitoring and Alerting Recommendations

### Metrics to Track

| Metric | Threshold | Alert Level |
|--------|-----------|-------------|
| Order rejection rate | > 5% in 5 minutes | Warning |
| Nonce sync failures | > 2 in 1 hour | Critical |
| WebSocket disconnections | > 5 in 5 minutes | Warning |
| Data staleness | > 5 seconds | Critical |
| Rate limit proximity | > 90% of limit | Warning |
| Position discrepancy | Any mismatch | Warning |
| Margin ratio | < 150% | Warning |
| Margin ratio | < 120% | Critical |

### Log Events (Structured Logging)

All events should include:
- Timestamp (ISO 8601)
- Correlation ID (trace orders through lifecycle)
- Market ID
- Order ID (when applicable)
- Event type
- Severity level

---

## 11. Summary of Critical Rules

1. **Never trust HTTP 200 as order confirmation** - Wait for WebSocket confirmation
2. **Never decrement nonce** - Nonces are consumed even on failure
3. **Never exceed rate limits** - Implement proactive throttling at 80%
4. **Never submit without validation** - Pre-validate all orders locally
5. **Never operate with stale data** - Halt when DataAge > 5 seconds
6. **Always reconcile on reconnection** - REST is source of truth
7. **Always serialize nonce operations** - Thread-safe nonce management
8. **Always log state transitions** - Full audit trail for debugging

---

## Document Metadata

- **Version**: 1.0
- **Created**: 2025-12-25
- **Author**: Trading Risk Manager Agent
- **Applies To**: GridBot Extended DEX Integration
- **Review Cycle**: Update when Extended API changes or after production incidents
