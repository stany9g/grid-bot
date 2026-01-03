# Extended DEX StarkEx Perpetual Order Message Hash Algorithm

## Date: 2026-01-02

## Executive Summary

This document describes the exact algorithm for computing the StarkEx perpetual order message hash used by Extended DEX (X10). The Python SDK uses `fast_stark_crypto.get_order_msg_hash()` from the Rust-based `stark-crypto-wrapper-py` library, NOT a custom SNIP-12 style Pedersen hash chain.

**The current C# `StarkSigner.cs` implementation is COMPLETELY WRONG and must be replaced.**

## The Correct Algorithm

### Source: Python SDK

**File:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\order_object_settlement.py`

```python
from fast_stark_crypto import get_order_msg_hash

def hash_order(
    amount_synthetic: StarkAmount,
    amount_collateral: StarkAmount,
    max_fee: StarkAmount,
    nonce: int,
    position_id: int,
    expiration_timestamp: datetime,
    public_key: int,
    starknet_domain: StarknetDomain,
) -> int:
    synthetic_asset = amount_synthetic.asset
    collateral_asset = amount_collateral.asset

    return get_order_msg_hash(
        position_id=position_id,
        base_asset_id=int(synthetic_asset.settlement_external_id, 16),
        base_amount=amount_synthetic.value,
        quote_asset_id=int(collateral_asset.settlement_external_id, 16),
        quote_amount=amount_collateral.value,
        fee_amount=max_fee.value,
        fee_asset_id=int(collateral_asset.settlement_external_id, 16),
        expiration=__calc_settlement_expiration(expiration_timestamp),
        salt=nonce,
        user_public_key=public_key,
        domain_name=starknet_domain.name,
        domain_version=starknet_domain.version,
        domain_chain_id=starknet_domain.chain_id,
        domain_revision=starknet_domain.revision,
    )
```

### Parameters (14 total)

| # | Parameter | Type | Description | Source |
|---|-----------|------|-------------|--------|
| 1 | `position_id` | int | Vault/position ID | Account `l2_vault` (from `/user/account/info`) |
| 2 | `base_asset_id` | int | Synthetic asset ID | Market `l2Config.syntheticId` (hex to int) |
| 3 | `base_amount` | int | Synthetic amount (SIGNED) | Calculated (see below) |
| 4 | `quote_asset_id` | int | Collateral asset ID | Market `l2Config.collateralId` (hex to int) |
| 5 | `quote_amount` | int | Collateral amount (SIGNED) | Calculated (see below) |
| 6 | `fee_amount` | int | Max fee amount | Calculated (ALWAYS positive) |
| 7 | `fee_asset_id` | int | Fee asset ID | Same as `quote_asset_id` |
| 8 | `expiration` | int | Settlement expiry | Order expiry + 14 days (IN SECONDS) |
| 9 | `salt` | int | Nonce | Random 32-bit unsigned integer |
| 10 | `user_public_key` | int | Stark public key | Account's public key |
| 11 | `domain_name` | str | Domain name | `"Perpetuals"` (FIXED) |
| 12 | `domain_version` | str | Domain version | `"v0"` (FIXED) |
| 13 | `domain_chain_id` | str | Chain identifier | `"SN_SEPOLIA"` (testnet) or `"SN_MAIN"` (mainnet) |
| 14 | `domain_revision` | str | Domain revision | `"1"` (FIXED) |

---

## Detailed Parameter Calculations

### 1. Position ID

**Source:** Account info response field `l2Vault`

```json
// GET /user/account/info response
{
  "data": {
    "l2Vault": "301301"  // This is the position_id
  }
}
```

**C# Equivalent:**
```csharp
var positionId = long.Parse(accountInfo.L2Vault);
```

---

### 2. Base Asset ID (Synthetic)

**Source:** Market `l2Config.syntheticId` - hexadecimal string

```json
// GET /info/markets response for BTC-USD
{
  "l2Config": {
    "syntheticId": "0x4254432d3600000000000000000000"
  }
}
```

**Python:**
```python
base_asset_id = int(synthetic_asset.settlement_external_id, 16)
# "0x4254432d3600000000000000000000" -> 1398167893655757028416000000000000000000
```

**C# Equivalent:**
```csharp
var baseAssetId = BigInteger.Parse(
    market.L2Config.SyntheticId.TrimStart("0x".ToCharArray()),
    NumberStyles.HexNumber);
```

---

### 3. Quote Asset ID (Collateral)

**Source:** Market `l2Config.collateralId` - hexadecimal string

```json
{
  "l2Config": {
    "collateralId": "0x31857064564ed0ff978e687456963cba09c2c6985d8f9300a1de4962fafa054"
  }
}
```

**C# Equivalent:**
```csharp
var quoteAssetId = BigInteger.Parse(
    market.L2Config.CollateralId.TrimStart("0x".ToCharArray()),
    NumberStyles.HexNumber);
```

---

### 4. Settlement Resolutions

From market `l2Config`:

| Field | Value | Use |
|-------|-------|-----|
| `syntheticResolution` | 1,000,000 | Multiply BTC quantity |
| `collateralResolution` | 1,000,000 | Multiply USD value |

**Example:** BTC-USD market
- `syntheticResolution` = 1,000,000
- `collateralResolution` = 1,000,000

---

### 5. Amount Conversions (CRITICAL)

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\assets.py`

```python
def convert_human_readable_to_stark_quantity(self, internal: Decimal, rounding_context: Context) -> int:
    return int(
        rounding_context.multiply(internal, Decimal(self.settlement_resolution)).to_integral(
            context=rounding_context
        )
    )
```

**Formula:**
```
stark_amount = floor(human_readable_amount * settlement_resolution)
```

**Example - BUY 0.001 BTC @ $80,000:**

```
synthetic_amount = 0.001 * 1,000,000 = 1,000
collateral_amount = (0.001 * 80000) * 1,000,000 = 80,000,000
fee_amount = (0.0005 * 80) * 1,000,000 = 40,000
```

**C# Equivalent:**
```csharp
public static long ToStarkAmount(decimal humanAmount, long resolution, MidpointRounding rounding = MidpointRounding.AwayFromZero)
{
    return (long)Math.Round(humanAmount * resolution, rounding);
}
```

---

### 6. Sign Convention (CRITICAL)

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\order_object_settlement.py` (lines 108-111)

```python
if is_buying_synthetic:  # BUY
    stark_collateral_amount = stark_collateral_amount.negate()  # quote is NEGATIVE
else:  # SELL
    stark_synthetic_amount = stark_synthetic_amount.negate()    # base is NEGATIVE
```

| Order Side | `base_amount` (synthetic) | `quote_amount` (collateral) |
|------------|---------------------------|----------------------------|
| **BUY**    | POSITIVE (receive BTC)    | NEGATIVE (pay USD)         |
| **SELL**   | NEGATIVE (pay BTC)        | POSITIVE (receive USD)     |

**Fee amount is ALWAYS POSITIVE.**

**Example - BUY 0.001 BTC @ $80,000:**
```
base_amount  = +1000       (positive - receiving BTC)
quote_amount = -80000000   (negative - paying USD)
fee_amount   = +40000      (positive - always)
```

**Example - SELL 0.001 BTC @ $80,000:**
```
base_amount  = -1000       (negative - paying BTC)
quote_amount = +80000000   (positive - receiving USD)
fee_amount   = +40000      (positive - always)
```

---

### 7. Rounding Contexts

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\amounts.py`

```python
ROUNDING_SELL_CONTEXT = decimal.Context(rounding=decimal.ROUND_DOWN)
ROUNDING_BUY_CONTEXT = decimal.Context(rounding=decimal.ROUND_UP)
ROUNDING_FEE_CONTEXT = decimal.Context(rounding=decimal.ROUND_UP)
```

| Context | Python | C# Equivalent |
|---------|--------|---------------|
| BUY amounts | `ROUND_UP` | `MidpointRounding.ToPositiveInfinity` |
| SELL amounts | `ROUND_DOWN` | `MidpointRounding.ToZero` |
| Fee amounts | `ROUND_UP` | `MidpointRounding.ToPositiveInfinity` |

---

### 8. Expiration Calculation (CRITICAL)

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\order_object_settlement.py` (lines 48-52)

```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

**Algorithm:**
1. Take the order's expiry timestamp (in UTC)
2. Add **14 days buffer**
3. Convert to Unix timestamp **in SECONDS** (not milliseconds!)
4. Round **UP** (ceiling)

**Example:**
```
Order expiry: 2024-01-19 01:08:57 UTC (expiryEpochMillis = 1705626537000)
+ 14 days:    2024-02-02 01:08:57 UTC
In seconds:   1706836137
```

**C# Equivalent:**
```csharp
public static long CalcSettlementExpiration(DateTimeOffset orderExpiry)
{
    var withBuffer = orderExpiry.AddDays(14);
    return (long)Math.Ceiling(withBuffer.ToUnixTimeSeconds() + withBuffer.Millisecond / 1000.0);
}
```

---

### 9. Nonce Generation

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\utils\nonce.py`

```python
def generate_nonce() -> int:
    return random.randint(0, 2**32 - 1)
```

**Range:** 0 to 4,294,967,295 (unsigned 32-bit integer)

**C# Equivalent:**
```csharp
public static long GenerateNonce()
{
    return Random.Shared.NextInt64(0, uint.MaxValue + 1L);
}
```

---

### 10. Domain Values (Starknet Domain)

**Source:** `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\configuration.py`

| Network | `name` | `version` | `chain_id` | `revision` |
|---------|--------|-----------|------------|------------|
| Testnet | `"Perpetuals"` | `"v0"` | `"SN_SEPOLIA"` | `"1"` |
| Mainnet | `"Perpetuals"` | `"v0"` | `"SN_MAIN"` | `"1"` |

**Note:** The domain name is `"Perpetuals"`, NOT `"Extended DEX"` as the C# code incorrectly uses.

---

## Complete Working Example

### Test Case from Python SDK

**File:** `C:\Users\stany\.claude\repos\python_sdk\tests\perpetual\test_order_object.py`

**Input:**
```python
# Frozen time: 2024-01-05 01:08:56.860694 UTC
# Nonce: 1473459052
trading_account = StarkPerpetualAccount(
    vault=10002,
    private_key="0x7a7ff6fd3cab02ccdcd4a572563f5976f8976899b03a39773795a3c486d4986",
    public_key="0x61c5e7e8339b7d56f197f54ea91b776776690e3232313de0f2ecbd0ef76f466",
    api_key="dummy_api_key",
)

# BTC-USD market from fixture:
# - syntheticId: "0x4254432d3600000000000000000000"
# - collateralId: "0x31857064564ed0ff978e687456963cba09c2c6985d8f9300a1de4962fafa054"
# - syntheticResolution: 1000000
# - collateralResolution: 1000000

order = create_order_object(
    account=trading_account,
    market=btc_usd_market,
    amount_of_synthetic=Decimal("0.00100000"),
    price=Decimal("43445.11680000"),
    side=OrderSide.BUY,
    expire_time=utc_now() + timedelta(days=14),  # 2024-01-19 01:08:56 UTC
    starknet_domain=TESTNET_CONFIG.starknet_domain,
)
```

**Expected Output (BUY order):**
```json
{
  "id": "2495374044666992118771096772295242242651427695217815113349321039194683172848",
  "market": "BTC-USD",
  "type": "LIMIT",
  "side": "BUY",
  "qty": "0.00100000",
  "price": "43445.11680000",
  "expiryEpochMillis": 1705626536861,
  "fee": "0.0005",
  "nonce": "1473459052",
  "settlement": {
    "signature": {
      "r": "0xa55625c7d5f1b85bed22556fc805224b8363074979cf918091d9ddb1403e13",
      "s": "0x504caf634d859e643569743642ccf244434322859b2421d76f853af43ae7a46"
    },
    "starkKey": "0x61c5e7e8339b7d56f197f54ea91b776776690e3232313de0f2ecbd0ef76f466",
    "collateralPosition": "10002"
  },
  "debuggingAmounts": {
    "collateralAmount": "-43445117",
    "feeAmount": "21723",
    "syntheticAmount": "1000"
  }
}
```

### Calculated Values Breakdown

For BUY 0.001 BTC @ $43,445.1168:

```
synthetic_amount:
  = 0.001 * 1,000,000 = 1000 (POSITIVE for BUY)

collateral_amount:
  = 0.001 * 43445.1168 * 1,000,000 = 43,445,116.8
  = 43,445,117 (rounded UP for BUY)
  = -43,445,117 (NEGATED for BUY)

fee_amount:
  = 0.0005 * 43.4451168 * 1,000,000 = 21,722.5584
  = 21,723 (rounded UP)

expiration:
  = 1705626536861 ms (order expiry) -> 1705626536.861 seconds
  + 14 days = 1706836136.861 seconds
  = 1706836137 (ceiling to seconds)
```

---

## C# Implementation Requirements

### 1. Replace Custom Hash with Native Library Call

The Python SDK uses `fast_stark_crypto.get_order_msg_hash()` which is a Rust implementation of the StarkEx perpetual order hash. You need to either:

**Option A: Port the Rust algorithm to C#**
- Requires understanding the internal structure of `get_order_msg_hash`
- Complex, but gives you full control

**Option B: Use a native library**
- Wrap the `fast_stark_crypto` library for C#
- Or find/create an equivalent .NET library

**Option C: Use the existing StarkNativeMethods if it has this function**
- Check if `signer-amd64.dll` has an equivalent function

### 2. New ExtendedOrderMessage Structure

```csharp
public sealed class StarkExOrderParams
{
    /// <summary>
    /// Vault/position ID from account l2Vault.
    /// </summary>
    public required long PositionId { get; init; }

    /// <summary>
    /// Synthetic asset ID (hex from l2Config.syntheticId converted to BigInteger).
    /// </summary>
    public required BigInteger BaseAssetId { get; init; }

    /// <summary>
    /// Collateral asset ID (hex from l2Config.collateralId converted to BigInteger).
    /// </summary>
    public required BigInteger QuoteAssetId { get; init; }

    /// <summary>
    /// Synthetic amount in Stark units (SIGNED: positive for BUY, negative for SELL).
    /// </summary>
    public required long BaseAmount { get; init; }

    /// <summary>
    /// Collateral amount in Stark units (SIGNED: negative for BUY, positive for SELL).
    /// </summary>
    public required long QuoteAmount { get; init; }

    /// <summary>
    /// Max fee amount in Stark units (ALWAYS positive).
    /// </summary>
    public required long FeeAmount { get; init; }

    /// <summary>
    /// Settlement expiration (order expiry + 14 days, IN SECONDS).
    /// </summary>
    public required long Expiration { get; init; }

    /// <summary>
    /// Nonce (random 32-bit unsigned integer).
    /// </summary>
    public required long Salt { get; init; }

    /// <summary>
    /// User's Stark public key.
    /// </summary>
    public required BigInteger UserPublicKey { get; init; }

    /// <summary>
    /// Whether this is testnet (affects chain_id).
    /// </summary>
    public required bool IsTestnet { get; init; }
}
```

### 3. Amount Calculator Helper

```csharp
public static class StarkAmountCalculator
{
    public static (long baseAmount, long quoteAmount, long feeAmount) CalculateStarkAmounts(
        decimal quantity,
        decimal price,
        decimal feeRate,
        bool isBuy,
        long syntheticResolution,
        long collateralResolution)
    {
        // Calculate human amounts
        var syntheticHuman = quantity;
        var collateralHuman = quantity * price;
        var feeHuman = feeRate * collateralHuman;

        // Convert to Stark amounts with appropriate rounding
        var rounding = isBuy ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToZero;

        var baseAmount = (long)Math.Round(syntheticHuman * syntheticResolution, rounding);
        var quoteAmount = (long)Math.Round(collateralHuman * collateralResolution, rounding);
        var feeAmount = (long)Math.Ceiling(feeHuman * collateralResolution);

        // Apply sign convention
        if (isBuy)
        {
            quoteAmount = -quoteAmount;  // Paying collateral
        }
        else
        {
            baseAmount = -baseAmount;    // Paying synthetic
        }

        return (baseAmount, quoteAmount, feeAmount);
    }

    public static long CalcSettlementExpiration(DateTimeOffset orderExpiry)
    {
        var withBuffer = orderExpiry.AddDays(14);
        return (long)Math.Ceiling((double)withBuffer.ToUnixTimeMilliseconds() / 1000.0);
    }

    public static long GenerateNonce()
    {
        return Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
    }
}
```

---

## Debugging Your Current Error

The server returned:
```json
{
  "positionId": "0x498f5",           // = 301301 - CORRECT
  "baseAssetId": "0x4254432d3600000000000000000000",  // CORRECT
  "baseAmount": 100,                  // WRONG: Should be ~1,000 for 0.001 BTC
  "quoteAmount": -8000000,            // WRONG: Should be ~-80,000,000 for $80k
  "feeAmount": "0x7d0",               // = 2000, WRONG: Should be ~40,000
  "expiration": "0x6973e579",         // Need to verify +14 days buffer
  "salt": "0x1"                       // WRONG: Should be random 32-bit
}
```

### Issues Found:

1. **baseAmount is 10x too small** - Missing one zero in resolution factor
2. **quoteAmount is 10x too small** - Missing one zero in resolution factor
3. **feeAmount is 20x too small** - Fee calculation error
4. **salt is hardcoded to 1** - Should be random 32-bit integer

---

## Summary of What's Wrong in Current C# Code

| Aspect | Current C# Code | Should Be |
|--------|-----------------|-----------|
| Hash algorithm | Custom Pedersen chain | `get_order_msg_hash()` native call |
| Domain name | `"Extended DEX"` | `"Perpetuals"` |
| Domain version | `"1"` (as int) | `"v0"` (as string) |
| Chain ID | `300`/`304` (int) | `"SN_SEPOLIA"`/`"SN_MAIN"` (string) |
| Amounts | Scaled by 10^8 | Scaled by settlement_resolution (10^6) |
| Sign convention | Not implemented | base/quote signs based on side |
| Expiration | Raw epoch millis | Epoch seconds + 14 days buffer |
| Nonce | Sequential | Random 32-bit |

---

## Files Referenced

| File | Purpose |
|------|---------|
| `python_sdk/x10/perpetual/order_object_settlement.py` | Hash computation, sign rules, expiry |
| `python_sdk/x10/perpetual/order_object.py` | Order creation factory |
| `python_sdk/x10/perpetual/amounts.py` | StarkAmount class, rounding contexts |
| `python_sdk/x10/perpetual/assets.py` | settlement_resolution conversion |
| `python_sdk/x10/perpetual/markets.py` | L2ConfigModel with asset IDs |
| `python_sdk/x10/perpetual/configuration.py` | Domain values |
| `python_sdk/x10/perpetual/accounts.py` | Signing with `fast_stark_crypto.sign()` |
| `python_sdk/x10/utils/nonce.py` | Nonce generation |
| `python_sdk/tests/perpetual/test_order_object.py` | Test cases with expected outputs |
| `python_sdk/tests/fixtures/markets.py` | BTC-USD market fixture with l2Config |
| `python_sdk/tests/fixtures/accounts.py` | Test account with vault, keys |

---

## Next Steps

1. **Research `get_order_msg_hash` implementation** - Check if StarkNativeMethods has equivalent
2. **Update StarkSigner.cs** - Replace custom hash with correct algorithm
3. **Create StarkAmountCalculator** - Handle resolution/rounding/sign correctly
4. **Update ExtendedOrderMessage** - Include all required fields
5. **Add unit tests** - Verify against Python SDK test cases
6. **Test with testnet** - Validate signature acceptance

---

## Python SDK Location

`C:\Users\stany\.claude\repos\python_sdk` (package: `x10-python-trading-starknet`)
