# Extended DEX Trading Fees Analysis

## Date: 2026-01-02

## Problem Statement
Extended DEX returning error: `{"status":"ERROR","error":{"code":1128,"message":"Trading fees are invalid"}}`

## Root Cause Analysis

After analyzing the Python SDK at `C:\Users\stany\.claude\repos\python_sdk`, I found the following key issues:

### 1. Fee Must Be Retrieved from API (PRIMARY CAUSE)

**The C# implementation uses a hardcoded fee rate:**
```csharp
// ExtendedConstants.cs line 81
public const decimal DefaultFeeRate = 0.0002m;  // 0.02%
```

**The Python SDK retrieves fees from the API per-market:**

From `x10/perpetual/trading_client/account_module.py` (lines 149-163):
```python
async def get_fees(
    self, *, market_names: List[str], builder_id: Optional[int] = None
) -> WrappedApiResponse[List[TradingFeeModel]]:
    """
    https://api.docs.extended.exchange/#get-fees
    """
    url = self._get_url(
        "/user/fees",
        query={
            "market": market_names,
            "builderId": builder_id,
        },
    )
    return await send_get_request(await self.get_session(), url, List[TradingFeeModel], api_key=self._get_api_key())
```

### 2. Fee Model Structure

From `x10/perpetual/fees.py`:
```python
class TradingFeeModel(X10BaseModel):
    market: str
    maker_fee_rate: Decimal
    taker_fee_rate: Decimal
    builder_fee_rate: Decimal
```

### 3. Default Fee Values (Fallback Only)

The Python SDK has default fees as **fallback only** (from `x10/perpetual/fees.py` lines 13-18):
```python
DEFAULT_FEES = TradingFeeModel(
    market="BTC-USD",
    maker_fee_rate=(Decimal("2") / Decimal("10000")),   # 0.0002 = 0.02%
    taker_fee_rate=(Decimal("5") / Decimal("10000")),   # 0.0005 = 0.05%
    builder_fee_rate=Decimal("0"),
)
```

**Key Insight**: The **taker fee** is `0.0005` (0.05%), NOT `0.0002` (0.02%)!

### 4. How Fees Are Used in Order Creation

From `x10/perpetual/order_object.py` (lines 66, 159, 213):
```python
# Get fee from account's trading_fee dict, or fall back to DEFAULT_FEES
fees = account.trading_fee.get(market.name, DEFAULT_FEES)

# Use the taker_fee_rate
fee_rate = fees.taker_fee_rate

# Pass fee_rate to the order
order = NewOrderModel(
    ...
    fee=fee_rate,  # This is the taker_fee_rate!
    ...
)
```

### 5. Account Stores Trading Fees Per-Market

From `x10/perpetual/accounts.py` (lines 16-53):
```python
class StarkPerpetualAccount:
    __trading_fee: Dict[str, TradingFeeModel]

    @property
    def trading_fee(self):
        return self.__trading_fee
```

The account maintains a dictionary of `market -> TradingFeeModel` that should be populated by calling `get_fees()` API.

## The Problem with C# Implementation

1. **Wrong Default Fee**: Uses `0.0002` (maker fee) instead of `0.0005` (taker fee)
2. **No API Call**: Doesn't fetch fees from `/user/fees` endpoint
3. **Static Fee**: Uses same fee for all markets instead of per-market fees

## Correct Fee Values

| Fee Type | Rate | Percentage |
|----------|------|------------|
| Maker Fee | 0.0002 | 0.02% |
| **Taker Fee** | **0.0005** | **0.05%** |
| Builder Fee | 0 | 0% |

**IMPORTANT**: When placing orders, you must use the **taker fee** rate (0.0005), not the maker fee rate.

## Solution Options

### Option A: Quick Fix - Use Correct Default (Minimum Change)
Change `ExtendedConstants.DefaultFeeRate` from `0.0002m` to `0.0005m`:
```csharp
public const decimal DefaultFeeRate = 0.0005m;  // Taker fee rate
```

### Option B: Proper Fix - Fetch Fees from API (Recommended)

1. Add `GetFeesAsync` method to `IExtendedHttpClient`:
```csharp
Task<IReadOnlyList<TradingFeeResponse>> GetFeesAsync(
    IReadOnlyList<string> markets,
    int? builderId = null,
    CancellationToken ct = default);
```

2. Create `TradingFeeResponse` model:
```csharp
public sealed record TradingFeeResponse
{
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    [JsonPropertyName("makerFeeRate")]
    public required decimal MakerFeeRate { get; init; }

    [JsonPropertyName("takerFeeRate")]
    public required decimal TakerFeeRate { get; init; }

    [JsonPropertyName("builderFeeRate")]
    public required decimal BuilderFeeRate { get; init; }
}
```

3. Cache fees per-market and use `TakerFeeRate` when creating orders

## API Endpoint Details

**Endpoint**: `GET /user/fees`

**Query Parameters**:
- `market`: List of market names (e.g., `["BTC-USD"]`)
- `builderId`: Optional builder ID for custom fee arrangements

**Response Format**:
```json
{
  "status": "OK",
  "data": [
    {
      "market": "BTC-USD",
      "makerFeeRate": "0.0002",
      "takerFeeRate": "0.0005",
      "builderFeeRate": "0"
    }
  ]
}
```

## Python SDK Files Analyzed

| File | Purpose |
|------|---------|
| `x10/perpetual/fees.py` | TradingFeeModel definition, DEFAULT_FEES |
| `x10/perpetual/order_object.py` | Order creation with fee lookup |
| `x10/perpetual/accounts.py` | StarkPerpetualAccount with trading_fee dict |
| `x10/perpetual/trading_client/account_module.py` | get_fees() API call |
| `x10/perpetual/order_object_settlement.py` | Fee calculation for signing |

## Validation Logic

The server validates that the submitted fee rate matches what's expected for the account:
1. Fees are account-specific (based on trading tier/volume)
2. The fee in the order must match the fee returned by `/user/fees` for that market
3. Using a wrong fee (like maker instead of taker) results in error 1128

## Recommendation

**Immediate Fix**: Change `DefaultFeeRate` to `0.0005m` (taker fee)

**Long-term Fix**: Implement proper fee fetching from the API and cache per-market fees, similar to how the Python SDK does it.

## Related Error Codes

| Code | Message | Cause |
|------|---------|-------|
| 1128 | Trading fees are invalid | Fee rate doesn't match server expectation |
| INVALID_FEE | (OrderStatusReason) | Order rejected due to invalid fee |
