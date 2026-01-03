# Extended DEX Order Hash Computation - Complete Analysis

## Date: 2026-01-02

## Executive Summary

The C# implementation has a **completely wrong order hash computation**. It uses a custom SNIP-12 style Pedersen hash chain, while the Python SDK uses `fast_stark_crypto.get_order_msg_hash()` which is a specialized StarkEx perpetual order hash function.

## Critical Finding: Wrong Hash Algorithm

### C# Implementation (WRONG)
```csharp
// ComputeOrderMessageHash in StarkSigner.cs
// Uses SNIP-12 style Pedersen hash chain:
var domainHash = ComputeDomainHash(); // Domain: "Extended DEX" + chain ID + version
var hash1 = PedersenHash(domainHash, starkPublicKey);
var marketHash = ComputeStringHash(order.Market);  // Hash of "BTC-USD" string
var hash2 = PedersenHash(hash1, marketHash);
var paramsHash = ComputeOrderParamsHash(order);  // side, type, price, qty, fee, reduceOnly
var hash3 = PedersenHash(hash2, paramsHash);
var nonceHex = ToFelt(order.Nonce);
var hash4 = PedersenHash(hash3, nonceHex);
var expiryHex = ToFelt(order.ExpiryEpochMillis);
var finalHash = PedersenHash(hash4, expiryHex);
```

### Python SDK Implementation (CORRECT)
```python
# From order_object_settlement.py, hash_order()
# Uses fast_stark_crypto.get_order_msg_hash():
get_order_msg_hash(
    position_id=position_id,                                    # int: vault ID
    base_asset_id=int(synthetic_asset.settlement_external_id, 16), # int: BTC asset ID
    base_amount=amount_synthetic.value,                         # int: SIGNED Stark amount
    quote_asset_id=int(collateral_asset.settlement_external_id, 16), # int: USD asset ID
    quote_amount=amount_collateral.value,                       # int: SIGNED Stark amount
    fee_amount=max_fee.value,                                   # int: positive fee
    fee_asset_id=int(collateral_asset.settlement_external_id, 16),  # int: USD asset ID
    expiration=__calc_settlement_expiration(expiration_timestamp), # int: seconds
    salt=nonce,                                                 # int: nonce
    user_public_key=public_key,                                 # int: Stark public key
    domain_name="Perpetuals",                                   # str
    domain_version="v0",                                        # str
    domain_chain_id="SN_SEPOLIA" or "SN_MAIN",                 # str
    domain_revision="1",                                        # str
)
```

---

## Exact Algorithm from Python SDK

### 1. Parameters for `get_order_msg_hash()`

| # | Parameter | Type | Source |
|---|-----------|------|--------|
| 1 | `position_id` | int | Account's `l2_vault` (vault ID) |
| 2 | `base_asset_id` | int | Market's `l2_config.synthetic_id` (hex to int) |
| 3 | `base_amount` | int | **SIGNED** Stark amount for synthetic (positive=BUY, negative=SELL) |
| 4 | `quote_asset_id` | int | Market's `l2_config.collateral_id` (hex to int) |
| 5 | `quote_amount` | int | **SIGNED** Stark amount for collateral (negative=BUY, positive=SELL) |
| 6 | `fee_amount` | int | **ALWAYS positive** max fee in Stark amount |
| 7 | `fee_asset_id` | int | Same as `quote_asset_id` (collateral asset) |
| 8 | `expiration` | int | Order expiry + 14 days buffer, in **SECONDS** |
| 9 | `salt` | int | Nonce |
| 10 | `user_public_key` | int | Stark public key |
| 11 | `domain_name` | str | `"Perpetuals"` |
| 12 | `domain_version` | str | `"v0"` |
| 13 | `domain_chain_id` | str | `"SN_SEPOLIA"` (testnet) or `"SN_MAIN"` (mainnet) |
| 14 | `domain_revision` | str | `"1"` |

### 2. Amount Conversion (Settlement Resolution)

From `assets.py` and `amounts.py`:

```python
# Asset class method
def convert_human_readable_to_stark_quantity(self, internal: Decimal, rounding_context: Context) -> int:
    return int(
        rounding_context.multiply(internal, Decimal(self.settlement_resolution)).to_integral(
            context=rounding_context
        )
    )
```

**Key insight**: `settlement_resolution` is the multiplier. For example:
- If BTC has `synthetic_resolution = 1_000_000_000` (1e9)
- Then 0.001 BTC = 0.001 * 1_000_000_000 = 1_000_000 Stark units

### 3. Sign Convention (CRITICAL)

From `order_object_settlement.py`, `create_order_settlement_data()`:

```python
is_buying_synthetic = side == OrderSide.BUY
rounding_context = ROUNDING_BUY_CONTEXT if is_buying_synthetic else ROUNDING_SELL_CONTEXT

# Calculate amounts (all positive initially)
stark_collateral_amount = collateral_amount_human.to_stark_amount(rounding_context=rounding_context)
stark_synthetic_amount = synthetic_amount_human.to_stark_amount(rounding_context=rounding_context)
stark_fee_amount = fee_amount_human.to_stark_amount(rounding_context=ROUNDING_FEE_CONTEXT)

# APPLY SIGN CONVENTION
if is_buying_synthetic:
    stark_collateral_amount = stark_collateral_amount.negate()  # NEGATIVE collateral (you pay)
else:
    stark_synthetic_amount = stark_synthetic_amount.negate()    # NEGATIVE synthetic (you sell)
```

**Sign Rules**:
| Side | base_amount (synthetic) | quote_amount (collateral) |
|------|------------------------|---------------------------|
| BUY  | **POSITIVE** (you receive BTC) | **NEGATIVE** (you pay USD) |
| SELL | **NEGATIVE** (you pay BTC) | **POSITIVE** (you receive USD) |

**Fee is ALWAYS positive** (worst-case maximum fee you might pay).

### 4. Expiration Calculation (CRITICAL)

From `order_object_settlement.py`:

```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

**Key points**:
1. **Add 14 days buffer** to the order expiry time
2. Convert to **seconds** (NOT milliseconds)
3. Use `math.ceil()` (round up)

**Example**:
- Order expires: 2026-01-09 00:00:00 UTC (7 days from now)
- Buffer: 2026-01-23 00:00:00 UTC (+ 14 days)
- Settlement expiration: `1737590400` (Unix timestamp in seconds)

### 5. Asset IDs

Asset IDs come from market data API `/info/markets`:

```json
{
  "name": "BTC-USD",
  "l2Config": {
    "type": "PERPETUAL",
    "collateralId": "0x1",
    "collateralResolution": 1000000,
    "syntheticId": "0x4254432d3600000000000000000000",
    "syntheticResolution": 1000000000
  }
}
```

**Conversion to int**:
- `base_asset_id = int("0x4254432d3600000000000000000000", 16)`
- `quote_asset_id = int("0x1", 16)` = 1

### 6. Rounding Contexts

```python
ROUNDING_SELL_CONTEXT = decimal.Context(rounding=decimal.ROUND_DOWN)
ROUNDING_BUY_CONTEXT = decimal.Context(rounding=decimal.ROUND_UP)
ROUNDING_FEE_CONTEXT = decimal.Context(rounding=decimal.ROUND_UP)
```

| Action | Rounding |
|--------|----------|
| BUY (synthetic amount) | ROUND_UP |
| BUY (collateral amount) | ROUND_UP |
| SELL (synthetic amount) | ROUND_DOWN |
| SELL (collateral amount) | ROUND_DOWN |
| Fee (always) | ROUND_UP |

---

## Complete Flow Example: BUY 0.001 BTC at $80,000

### Step 1: Get Market Data
```json
{
  "name": "BTC-USD",
  "l2Config": {
    "collateralId": "0x1",
    "collateralResolution": 1000000,
    "syntheticId": "0x4254432d3600000000000000000000",
    "syntheticResolution": 1000000000
  }
}
```

### Step 2: Get Account Data
```json
{
  "accountId": 201301,
  "l2Key": "0x123...abc",
  "l2Vault": "301301"
}
```

### Step 3: Calculate Amounts

```python
# Inputs
qty = Decimal("0.001")  # BTC
price = Decimal("80000")  # USD
taker_fee_rate = Decimal("0.0005")

# Synthetic (base) amount
synthetic_amount_human = qty = 0.001
synthetic_stark = 0.001 * 1_000_000_000 = 1_000_000

# Collateral (quote) amount
collateral_amount_human = qty * price = 0.001 * 80000 = 80
collateral_stark = 80 * 1_000_000 = 80_000_000

# Fee amount
fee_amount_human = taker_fee_rate * collateral_amount_human = 0.0005 * 80 = 0.04
fee_stark = 0.04 * 1_000_000 = 40_000

# Apply sign for BUY
base_amount = +1_000_000      # POSITIVE (receiving BTC)
quote_amount = -80_000_000    # NEGATIVE (paying USD)
fee_amount = +40_000          # POSITIVE (always)
```

### Step 4: Calculate Expiration

```python
# Order expires in 7 days
order_expiry = datetime.now(UTC) + timedelta(days=7)

# Add 14 day buffer
settlement_expiry = order_expiry + timedelta(days=14)

# Convert to seconds (ceil)
expiration = math.ceil(settlement_expiry.timestamp())
# e.g., 1737590400
```

### Step 5: Generate Nonce

```python
nonce = random.randint(0, 2**32 - 1)
# e.g., 1234567890
```

### Step 6: Compute Hash

```python
order_hash = get_order_msg_hash(
    position_id=301301,
    base_asset_id=0x4254432d3600000000000000000000,  # BTC
    base_amount=1000000,                             # POSITIVE
    quote_asset_id=0x1,                              # USD
    quote_amount=-80000000,                          # NEGATIVE
    fee_amount=40000,                                # POSITIVE
    fee_asset_id=0x1,                                # USD
    expiration=1737590400,                           # seconds
    salt=1234567890,                                 # nonce
    user_public_key=0x123...abc,
    domain_name="Perpetuals",
    domain_version="v0",
    domain_chain_id="SN_SEPOLIA",                    # testnet
    domain_revision="1",
)
```

### Step 7: Sign Hash

```python
(r, s) = fast_stark_crypto.sign(private_key=stark_private_key, msg_hash=order_hash)
```

---

## Comparison: Your Debug Data vs Python SDK

### Your Current Debug Output
```json
{
  "positionId": {"value": {"value": "0x498f5"}},  // = 301301 decimal - CORRECT
  "baseAssetId": {"value": "0x4254432d3600000000000000000000"},  // BTC - CORRECT
  "baseAmount": {"_value": 100},                   // WAY TOO SMALL
  "quoteAssetId": {"value": "0x1"},               // USD - CORRECT
  "quoteAmount": {"_value": -8000000},            // -8M, but should be based on qty
  "feeAssetId": {"value": "0x1"},                 // USD - CORRECT
  "feeAmount": "0x7d0",                           // = 2000 decimal
  "expiration": {"seconds": "0x6973e579"},        // = 1765885305 - check if +14 days
  "salt": "0x1",                                  // = 1, should be random
  "signature": ["0x...", "0x..."]
}
```

### Issues Identified

| Field | Your Value | Expected (0.001 BTC @ $80k) | Issue |
|-------|------------|----------------------------|-------|
| `baseAmount` | 100 | 1,000,000 | Missing settlement_resolution (1e9) |
| `quoteAmount` | -8,000,000 | -80,000,000 | Off by factor of 10 |
| `feeAmount` | 2,000 | ~40,000 | Fee calculation wrong |
| `salt` | 1 | random 32-bit int | Not random |

---

## C# Implementation Requirements

### Required Parameters

```csharp
public sealed record StarkOrderHashParams
{
    // From account info
    public required long PositionId { get; init; }           // l2Vault as long
    public required BigInteger UserPublicKey { get; init; }  // l2Key as BigInteger

    // From market info l2Config
    public required BigInteger BaseAssetId { get; init; }    // syntheticId hex to BigInteger
    public required long BaseResolution { get; init; }       // syntheticResolution
    public required BigInteger QuoteAssetId { get; init; }   // collateralId hex to BigInteger
    public required long QuoteResolution { get; init; }      // collateralResolution

    // Order params
    public required decimal Quantity { get; init; }          // In human-readable (0.001)
    public required decimal Price { get; init; }             // In human-readable (80000)
    public required decimal FeeRate { get; init; }           // Taker fee rate (0.0005)
    public required bool IsBuy { get; init; }
    public required DateTime Expiry { get; init; }           // Order expiry (NOT settlement)
    public required long Nonce { get; init; }                // Random 32-bit

    // Domain
    public required string DomainChainId { get; init; }      // "SN_SEPOLIA" or "SN_MAIN"
}
```

### Amount Calculation

```csharp
public static (long baseAmount, long quoteAmount, long feeAmount) CalculateStarkAmounts(
    decimal quantity,
    decimal price,
    decimal feeRate,
    long baseResolution,
    long quoteResolution,
    bool isBuy)
{
    // Calculate human amounts
    var collateralHuman = quantity * price;
    var feeHuman = collateralHuman * feeRate;

    // Convert to Stark amounts (using ceiling for buy, floor for sell)
    var roundingMode = isBuy ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToNegativeInfinity;

    var baseStark = (long)Math.Round(quantity * baseResolution, roundingMode);
    var quoteStark = (long)Math.Round(collateralHuman * quoteResolution, roundingMode);
    var feeStark = (long)Math.Ceiling(feeHuman * quoteResolution);  // Always round up

    // Apply sign convention
    if (isBuy)
    {
        return (baseStark, -quoteStark, feeStark);  // Positive base, negative quote
    }
    else
    {
        return (-baseStark, quoteStark, feeStark);  // Negative base, positive quote
    }
}
```

### Expiration Calculation

```csharp
public static long CalculateSettlementExpiration(DateTime orderExpiry)
{
    // Add 14 day buffer
    var settlementExpiry = orderExpiry.AddDays(14);

    // Convert to Unix seconds (ceiling)
    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var seconds = (long)Math.Ceiling((settlementExpiry.ToUniversalTime() - epoch).TotalSeconds);

    return seconds;
}
```

### Native Function Call

The C# implementation needs a native library that exposes `get_order_msg_hash()` with these exact parameters:

```csharp
[DllImport("stark_crypto")]
public static extern IntPtr get_order_msg_hash(
    string position_id,           // decimal string
    string base_asset_id_hex,     // hex string "0x..."
    string base_amount,           // decimal string (can be negative)
    string quote_asset_id_hex,    // hex string "0x..."
    string quote_amount,          // decimal string (can be negative)
    string fee_asset_id_hex,      // hex string "0x..."
    string fee_amount,            // decimal string (positive)
    string expiration,            // decimal string
    string salt,                  // decimal string
    string user_public_key_hex,   // hex string "0x..."
    string domain_name,           // "Perpetuals"
    string domain_version,        // "v0"
    string domain_chain_id,       // "SN_SEPOLIA" or "SN_MAIN"
    string domain_revision        // "1"
);
```

---

## Action Items

1. **Replace entire hash computation logic** in `StarkSigner.cs`
2. **Use native `get_order_msg_hash()`** function from a compatible library
3. **Fix amount conversions** using `settlement_resolution` from market data
4. **Apply correct sign convention** based on order side
5. **Calculate settlement expiration** with +14 day buffer in seconds
6. **Generate random nonce** using `Random.Shared.Next(0, int.MaxValue)`
7. **Use correct domain** values from `configuration.py`

---

## Python SDK Reference Files

| File | Path | Purpose |
|------|------|---------|
| Order Settlement | `x10/perpetual/order_object_settlement.py` | Hash computation, sign convention |
| Amounts | `x10/perpetual/amounts.py` | Stark amount conversion |
| Assets | `x10/perpetual/assets.py` | Settlement resolution |
| Markets | `x10/perpetual/markets.py` | Asset from market l2_config |
| Configuration | `x10/perpetual/configuration.py` | Domain values |
| Accounts | `x10/perpetual/accounts.py` | Signing |
| Nonce | `x10/utils/nonce.py` | Random nonce generation |
| Order Object | `x10/perpetual/order_object.py` | Full order creation flow |

## Rust Library Reference

| Function | Repository | Purpose |
|----------|------------|---------|
| `rs_get_order_msg` | x10xchange/stark-crypto-wrapper-py | Message hash |
| `sign` | x10xchange/stark-crypto-wrapper-py | ECDSA signing |

---

## Sources

- [fast_stark_crypto lib.py](https://github.com/x10xchange/stark-crypto-wrapper-py/blob/starknet/python/fast_stark_crypto/lib.py)
- [fast_stark_crypto lib.rs](https://github.com/x10xchange/stark-crypto-wrapper-py/blob/starknet/src/lib.rs)
- Python SDK at `.claude/repos/python_sdk`
