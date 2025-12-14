# Lighter DEX UpdateLeverage Research

## Problem Statement

The trading bot encounters error code **21501 "invalid tx info"** when attempting to update leverage with the following request:
- MarketIndex: 1
- InitialMarginFraction: 5000 (intended for 2x leverage)
- MarginMode: Cross (0)

## Research Questions

1. What is the correct format for UpdateLeverage transaction on Lighter DEX?
2. What does InitialMarginFraction represent exactly?
3. Is 5000 the right value for 2x leverage?
4. Are there prerequisites for updating leverage?
5. What does error code 21501 mean?

---

## Findings

### 1. Error Code 21501 Definition

From [Lighter API Documentation](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors):

| Error Code | Name | Message |
|------------|------|---------|
| **21501** | `AppErrInvalidTxInfo` | "invalid tx info" |

**Meaning:** The transaction info structure is malformed or contains invalid parameters. The signing succeeds (native library produces txInfo), but the server rejects it during validation.

**Common causes for 21501:**
1. Transaction parameters don't match server expectations
2. Parameter values are outside valid ranges
3. Required fields are missing or malformed
4. State-based rejections (e.g., operation not applicable in current account state)

### 2. InitialMarginFraction Parameter

**How it works on Lighter DEX:**

Lighter uses a **basis point scale** where 10000 = 100%:
- InitialMarginFraction represents the **initial margin percentage** in basis points
- The relationship to leverage: `Leverage = 10000 / InitialMarginFraction`
- Conversely: `InitialMarginFraction = 10000 / Leverage`

**Examples:**
| Leverage | InitialMarginFraction | Margin % |
|----------|----------------------|----------|
| 50x | 200 | 2% |
| 25x | 400 | 4% |
| 20x | 500 | 5% |
| 10x | 1000 | 10% |
| 5x | 2000 | 20% |
| **2x** | **5000** | **50%** |
| 1x (no leverage) | 10000 | 100% |

**Current implementation is CORRECT:**
```csharp
// TradingBotHostedService.cs:342
var initialMarginFraction = (int)(10000m / maxLeverage);
```

For 2x leverage: `10000 / 2 = 5000` - this is mathematically correct.

### 3. Market-Specific Leverage Limits

**CRITICAL FINDING:** Each market has **minimum margin requirements** that limit maximum leverage.

From [Contract Specifications](https://docs.lighter.xyz/perpetual-futures/contract-specifications):

| Asset | Max Leverage | Min IMR | Min MMR |
|-------|-------------|---------|---------|
| BTC, ETH | 50x | 2% (200) | 1.2% |
| SOL, Forex | 25x | 4% (400) | 2.4% |
| XRP, HYPE, BNB | 20x | 5% (500) | 3% |
| ARB, OP, XAU | 15x | 6.67% (~667) | 4% |
| Most altcoins | 10x | 10% (1000) | 6% |
| Lower-liquidity assets | 8x/5x/3x | Variable | Variable |

**User leverage constraint:**
> Users can change their leverage on the markets. In that case, the initial margin fraction of the market will be `min(margin fraction set by the user, minimum fraction allowed for the market)`.

**This means:**
- You CANNOT set leverage HIGHER than the market's maximum
- You CAN set leverage LOWER than the market's maximum
- For Market 1 (ETH), max leverage is 50x (min IMR = 200)
- Setting 5000 (2x leverage) is VALID because it's more conservative than 200 (50x)

### 4. MarginMode Values

Based on the native library signature and error codes:

| Value | Mode | Description |
|-------|------|-------------|
| 0 | Cross | Shared margin across all positions |
| 1 | Isolated | Margin isolated per position |

Error code 21613 indicates "invalid margin mode" - values must be 0 or 1.

### 5. Transaction Type

From `TransactionTypes.cs`:
```csharp
public const int UpdateLeverage = 20;
```

Native library signature (`NativeMethods.cs:302-308`):
```csharp
[DllImport(LibraryName)]
internal static extern SignedTxResponse SignUpdateLeverage(
    int marketIndex,
    int initialMarginFraction,
    int marginMode,
    long nonce,
    int apiKeyIndex,
    long accountIndex);
```

### 6. Possible Causes of Error 21501 for UpdateLeverage

**Hypothesis 1: Position Already Has Leverage Set**
The error might occur if trying to update leverage to a value it's already set to, or if there's a pending leverage update.

**Hypothesis 2: Open Position Constraint**
Some exchanges prevent leverage changes while there's an open position. Lighter MAY have similar restrictions.

**Hypothesis 3: Order Margin Impact**
If changing leverage would cause the account to fall below initial margin requirements with existing orders, the update may be rejected.

**Hypothesis 4: Invalid Market Index**
Market Index 1 should be valid (ETH), but verify the market exists.

**Hypothesis 5: Transaction Info Encoding Issue**
The native signer might produce txInfo that's malformed for specific parameter combinations.

### 7. Prerequisites for Updating Leverage

Based on related error codes:
- **21507**: Account must NOT be below maintenance margin
- **21508**: Account must NOT be below initial margin
- **21613**: Margin mode must be valid (0 or 1)
- **21604**: Margin fractions must be valid for the market

**Recommended checks before calling UpdateLeverage:**
1. Verify account has sufficient margin health
2. Verify market exists and is active
3. Verify the new InitialMarginFraction is >= market minimum
4. Consider closing or reducing positions if margin would be insufficient

---

## Recommended Debugging Steps

### Step 1: Log the Actual txInfo Being Sent

```csharp
var (txInfo, error) = await _signer.UpdateLeverageAsync(request);
_logger.LogDebug(
    "UpdateLeverage txInfo for market {MarketId}: IMF={IMF}, Mode={Mode}, txInfo={TxInfo}",
    request.MarketIndex, request.InitialMarginFraction, request.MarginMode, txInfo);
```

### Step 2: Verify Market Exists

Before calling UpdateLeverage, verify the market:
```csharp
var markets = await _queryClient.GetOrderBooksAsync(ct);
var market = markets.FirstOrDefault(m => m.MarketId == request.MarketIndex);
if (market == null)
{
    _logger.LogError("Market {MarketId} not found", request.MarketIndex);
    return;
}
```

### Step 3: Check Current Account State

```csharp
var account = _state.GetAccount();
var userStats = await GetUserStatsAsync();
_logger.LogInformation(
    "Account state before leverage update: Collateral={Collateral}, AvailableBalance={Available}",
    userStats?.Collateral, userStats?.AvailableBalance);
```

### Step 4: Test with Different Parameters

Try updating to a different leverage value:
```csharp
// Try 5x instead of 2x
var initialMarginFraction = 2000; // 5x leverage = 20% margin
```

### Step 5: Test Without Position

Ensure there's no open position in the market when testing:
```csharp
var position = _state.GetPositionSize(marketId);
if (position.HasValue && position.Value != 0)
{
    _logger.LogWarning(
        "Position exists in market {MarketId}: Size={Size}. Leverage change may fail.",
        marketId, position.Value);
}
```

---

## Potential Fixes

### Fix 1: Handle 21501 Gracefully for Leverage Updates

```csharp
public async Task SetInitialLeverageAsync(int marketId, CancellationToken ct = default)
{
    try
    {
        var result = await _commandClient.UpdateLeverageAsync(request, ct);
        // Success handling...
    }
    catch (LighterApiException ex) when (ex.Code == 21501)
    {
        // Error 21501 may indicate leverage is already set or another state issue
        _logger.LogWarning(
            "UpdateLeverage returned 21501 for market {MarketId} - " +
            "leverage may already be configured or account state prevents update",
            marketId);
        // Continue without failing - leverage might already be correct
    }
}
```

### Fix 2: Query Current Leverage Before Updating

If possible, check current leverage setting and only update if different:
```csharp
// Check if leverage is already set (if API supports it)
var currentLeverage = await GetCurrentLeverageAsync(marketId);
if (currentLeverage == targetLeverage)
{
    _logger.LogInformation("Leverage already set to {Leverage}x for market {MarketId}",
        targetLeverage, marketId);
    return;
}
```

### Fix 3: Retry with Nonce Sync

Like other transactions, implement retry with nonce synchronization:
```csharp
// Already implemented in WsLighterCommandClient.ExecuteWithNonceRetryAsync
// Error 21501 is NOT a nonce error (21104), so retry won't help here
```

---

## Summary

| Question | Answer |
|----------|--------|
| Is 5000 correct for 2x leverage? | **YES** - 10000/2 = 5000 |
| Is MarginMode 0 (Cross) valid? | **YES** - 0=Cross, 1=Isolated |
| What is error 21501? | "invalid tx info" - malformed TX or state-based rejection |
| Prerequisites for UpdateLeverage? | Account above margin requirements, valid market, valid parameters |
| Why might it fail? | Account state, existing position, leverage already set, or API limitation |

## Recommended Actions

1. **IMMEDIATE**: Add detailed logging of txInfo and account state before leverage update
2. **IMMEDIATE**: Handle error 21501 gracefully (non-fatal) and continue bot operation
3. **INVESTIGATE**: Check if Lighter allows leverage changes with open positions
4. **INVESTIGATE**: Check if there's a way to query current leverage setting
5. **TEST**: Try on testnet without any position in the market
6. **CONTACT**: Consider reaching out to Lighter support for clarification on 21501 for UpdateLeverage

---

## References

- [Lighter Contract Specifications](https://docs.lighter.xyz/perpetual-futures/contract-specifications)
- [Lighter API Error Codes](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors)
- [Lighter Go SDK](https://github.com/elliottech/lighter-go)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)

---

## Open Questions

1. **Can leverage be updated with an open position?** - Not documented
2. **Does Lighter return 21501 if leverage is already set?** - Need to test
3. **Is there an API to query current leverage setting?** - Not found in docs
4. **Does Cross margin mode require different handling than Isolated?** - Unknown
