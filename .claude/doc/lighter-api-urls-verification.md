# Lighter DEX API URLs Verification

**Date:** 2025-12-26
**Requested by:** Parent agent for runtime network switching implementation

## Confirmed API URLs

### REST API Base URLs

| Network | Base URL | Chain ID |
|---------|----------|----------|
| **Testnet** | `https://testnet.zklighter.elliot.ai` | 300 |
| **Mainnet** | `https://mainnet.zklighter.elliot.ai` | 304 |

**Your current configuration is CORRECT.**

### WebSocket URLs

| Network | WebSocket URL |
|---------|---------------|
| **Testnet** | `wss://testnet.zklighter.elliot.ai/stream` |
| **Mainnet** | `wss://mainnet.zklighter.elliot.ai/stream` |

The WebSocket URL is derived by:
1. Replacing `https://` with `wss://`
2. Appending `/stream` to the path

This derivation logic is already implemented in `LighterWebSocketClient.GetWebSocketUrl()`.

## Endpoint Paths

**No differences between testnet and mainnet.** All REST API paths are identical:

| Category | Path Prefix | Example Endpoints |
|----------|-------------|-------------------|
| Accounts | `/api/v1/` | `account`, `apikeys` |
| Markets | `/api/v1/` | `orderbooks`, `markets`, `candlesticks` |
| Transactions | `/api/v1/` | `sendTx`, `sendTxBatch`, `next_nonce` |
| Public Data | `/api/v1/` | `orderbook`, `trades`, `ticker` |

## Network-Specific Considerations

### 1. Chain ID Must Match Network
- Testnet: `ChainId = 300`
- Mainnet: `ChainId = 304`

The chain ID is embedded in transaction signatures. Using wrong chain ID will cause signature verification failures.

### 2. Account Separation
- Testnet and mainnet accounts are completely separate
- Same private key will derive different account indices on each network
- API keys generated on testnet do not work on mainnet (and vice versa)

### 3. Rate Limits (Same on Both Networks)
| Endpoint Type | Limit |
|---------------|-------|
| REST general | Per-minute quota |
| `sendTx`/`sendTxBatch` | Volume-based quota (1 USDC volume = 1 tx quota) |
| WebSocket sessions | 100 per IP |
| WebSocket subscriptions | 1000 per IP |
| Unique accounts per WebSocket | 10 |

### 4. Order Expiry (No Difference Found)
Based on documentation review, both networks support the default 28-day expiry (`-1` in the SDK). The codebase references this in `OrderConstants.Default28DayOrderExpiry`.

### 5. Asset/Market Differences
- Testnet may have fewer markets or test tokens
- Market indices may differ between networks
- Always query `/api/v1/orderbooks` after network switch to get current market list

## Runtime Switching Implementation Notes

When switching networks at runtime:

1. **Stop all trading activity** before switching
2. **Close WebSocket connections** - cannot reuse across networks
3. **Update configuration atomically:**
   - `ApiUrl`
   - `ChainId`
   - `PrivateKey` (if using different keys per network)
   - `AccountIndex` (will be different)
   - `ApiKeyIndex` (if using API keys)
4. **Refresh market data** - market indices may differ
5. **Clear any cached nonces** - nonces are per-API-key and network
6. **Re-establish WebSocket subscriptions**

## Validation Checklist

Before allowing network switch:
- [ ] Bot is stopped (no active orders)
- [ ] All pending orders cancelled
- [ ] WebSocket disconnected
- [ ] Configuration validated for target network
- [ ] Credentials available for target network

## References

- Official API Docs: https://apidocs.lighter.xyz
- WebSocket Reference: https://apidocs.lighter.xyz/docs/websocket-reference
- Rate Limits: https://apidocs.lighter.xyz/docs/rate-limits
- Current implementation: `GridBot.Lighter/LighterWebSocketClient.cs` (line 309-321)
