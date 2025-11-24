# GridBot.Lighter

C# wrapper for the [Lighter Protocol](https://lighter.xyz) native signing library. Provides a type-safe, async/await API for signing cryptocurrency trading transactions.

## Overview

This library wraps the official Lighter signing library (written in Go, compiled to native binaries) and provides a modern C# API with:

- **Platform Detection**: Automatically loads the correct native library for Windows (.dll) or Linux (.so)
- **Type-Safe API**: Strongly-typed request models with validation
- **Async/Await**: All operations return `Task<>` for non-blocking execution
- **Result Tuples**: Returns `(result, error)` tuples instead of throwing exceptions
- **Thread-Safe Nonce Management**: Automatic nonce handling for transaction signing

## Architecture

```
GridBot.Lighter/
├── Native/
│   ├── NativeMethods.cs       # P/Invoke declarations with platform detection
│   ├── NativeStructs.cs       # Marshaling structs for native interop
│   ├── signer-amd64.dll       # Windows native library
│   └── signer-amd64.so        # Linux native library
├── Models/
│   ├── Enums.cs              # OrderType, TimeInForce, MarginMode, etc.
│   └── OrderRequest.cs       # High-level request models
└── SignerClient.cs           # Main public API
```

## Supported Operations

### Core Trading (Currently Implemented)
- ✅ Generate API key pair
- ✅ Create single order (limit, market, stop-loss, take-profit, etc.)
- ✅ Create grouped orders (OCO, OTO, OTOCO)
- ✅ Cancel specific order
- ✅ Cancel all orders in a market
- ✅ Modify existing order
- ✅ Update position leverage

### Future Operations (Native Library Supports)
- ⏳ Withdraw funds
- ⏳ Transfer between accounts
- ⏳ Create sub-accounts
- ⏳ Liquidity pool operations (create, update, mint/burn shares)
- ⏳ Change public key
- ⏳ Create authentication token

## Usage

### 1. Generate API Key Pair

```csharp
using GridBot.Lighter;

// Generate a new key pair
var (privateKey, publicKey, error) = await SignerClient.GenerateApiKeyAsync();

if (error != null)
{
    Console.WriteLine($"Error: {error}");
    return;
}

Console.WriteLine($"Private Key: {privateKey}");
Console.WriteLine($"Public Key: {publicKey}");
```

### 2. Initialize Client

```csharp
using var signer = new SignerClient();

var initError = await signer.InitializeAsync(
    url: "https://api.lighter.xyz",
    privateKey: "your-api-private-key",
    chainId: ChainId.Mainnet,        // or ChainId.Testnet
    apiKeyIndex: 0,
    accountIndex: 0,
    initialNonce: 0
);

if (initError != null)
{
    Console.WriteLine($"Init failed: {initError}");
    return;
}
```

### 3. Create a Limit Order

```csharp
using GridBot.Lighter.Models;

var orderRequest = new CreateOrderRequest
{
    MarketIndex = 0,                    // BTC-USDC
    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    BaseAmount = 1_000_000,             // Order size (scaled)
    Price = 50_000,                     // Order price (scaled)
    IsAsk = false,                      // false = buy, true = sell
    OrderType = OrderType.Limit,
    TimeInForce = TimeInForce.GoodTillTime,
    OrderExpiry = OrderConstants.Default28DayOrderExpiry
};

var (txInfo, error) = await signer.CreateOrderAsync(orderRequest);

if (error != null)
{
    Console.WriteLine($"Order failed: {error}");
    return;
}

Console.WriteLine($"Order signed: {txInfo}");
// Send txInfo to Lighter API to submit the order
```

### 4. Create Grouped Orders (OCO)

One-Cancels-Other: When one order fills, the other is cancelled.

```csharp
var ocoRequest = new CreateGroupedOrdersRequest
{
    GroupingType = GroupingType.OneCancelOther,
    Orders = new List<CreateOrderRequest>
    {
        // Take-profit order
        new CreateOrderRequest
        {
            MarketIndex = 0,
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BaseAmount = 1_000_000,
            Price = 55_000,
            IsAsk = true,                       // Sell
            OrderType = OrderType.TakeProfitLimit
        },
        // Stop-loss order
        new CreateOrderRequest
        {
            MarketIndex = 0,
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1,
            BaseAmount = 1_000_000,
            Price = 45_000,
            IsAsk = true,                       // Sell
            OrderType = OrderType.StopLossLimit,
            TriggerPrice = 45_000
        }
    }
};

var (txInfo, error) = await signer.CreateGroupedOrdersAsync(ocoRequest);
```

### 5. Cancel Order

```csharp
var (txInfo, error) = await signer.CancelOrderAsync(
    marketIndex: 0,
    orderId: 12345
);
```

### 6. Cancel All Orders

```csharp
var (txInfo, error) = await signer.CancelAllOrdersAsync(
    marketIndex: 0,
    timeInForce: 0  // 0 = immediate
);
```

### 7. Modify Existing Order

```csharp
var modifyRequest = new ModifyOrderRequest
{
    MarketIndex = 0,
    OrderId = 12345,
    NewClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    NewBaseAmount = 2_000_000,
    NewPrice = 51_000
};

var (txInfo, error) = await signer.ModifyOrderAsync(modifyRequest);
```

### 8. Update Position Leverage

```csharp
var leverageRequest = new UpdateLeverageRequest
{
    MarketIndex = 0,
    MarginMode = MarginMode.Isolated,
    Leverage = 10
};

var (txInfo, error) = await signer.UpdateLeverageAsync(leverageRequest);
```

## Enumerations

### OrderType
- `Limit` - Execute at specified price or better
- `Market` - Execute immediately at market price
- `StopLoss` - Trigger when price reaches stop level
- `StopLossLimit` - Become limit order at stop price
- `TakeProfit` - Trigger at profit target
- `TakeProfitLimit` - Become limit order at target
- `TWAP` - Time-weighted average price

### TimeInForce
- `ImmediateOrCancel` - Execute immediately, cancel remainder
- `GoodTillTime` - Active until expiry timestamp
- `PostOnly` - Only add liquidity (cancel if would match)

### MarginMode
- `Cross` - All account balance used as margin
- `Isolated` - Only allocated margin at risk

### GroupingType
- `OneTriggerOther` (OTO) - When first fills, second is placed
- `OneCancelOther` (OCO) - When one fills, other is cancelled
- `OneTriggerOneCancelOther` (OTOCO) - Combined OTO + OCO

## Constants

```csharp
ChainId.Mainnet                          // 304 (Polygon)
ChainId.Testnet                          // 300 (Polygon Mumbai)
OrderConstants.NilTriggerPrice           // 0
OrderConstants.Default28DayOrderExpiry   // -1
OrderConstants.DefaultIocExpiry          // 0
OrderConstants.UsdcTickerScale           // 1,000,000
```

## Error Handling

All operations return `(result, error)` tuples. Always check the error:

```csharp
var (txInfo, error) = await signer.CreateOrderAsync(request);

if (error != null)
{
    // Handle error - could be:
    // - Validation error (e.g., "Price must be positive")
    // - Native library error (e.g., signing failed)
    // - Initialization error (e.g., "Client not initialized")
    Console.WriteLine($"Error: {error}");
    return;
}

// Use txInfo - contains signed transaction data to submit to API
ProcessTransaction(txInfo);
```

## Native Library Requirements

The native libraries are platform-specific:

- **Windows**: `signer-amd64.dll` (x86_64)
- **Linux**: `signer-amd64.so` (x86_64)
- **macOS**: `signer-arm64.dylib` (ARM64) - Not included, but supported

The correct library is automatically loaded based on the runtime platform. Libraries must be present in the `Native/` folder of the output directory.

## Building

```bash
dotnet build GridBot.Lighter/GridBot.Lighter.csproj
```

The `.csproj` is configured to automatically copy the correct native library to the output directory based on the build platform.

## Testing

See `GridBot.ApiService/Examples/SignerUsageExample.cs` for comprehensive usage examples.

To test native library loading:
1. Build the project
2. Verify native binaries are copied to `bin/Debug/net10.0/Native/`
3. Run any example method to test the signing operations

## Integration with REST API

The `SignerClient` only signs transactions locally. To submit orders to Lighter:

1. Sign the transaction with `SignerClient`
2. Receive `txInfo` string
3. Send `txInfo` to Lighter REST API endpoints (e.g., `/orders/create`)

Example integration:

```csharp
// 1. Sign locally
var (txInfo, error) = await signer.CreateOrderAsync(request);
if (error != null) return;

// 2. Submit to API
using var httpClient = new HttpClient();
var response = await httpClient.PostAsJsonAsync(
    "https://api.lighter.xyz/orders/create",
    new { tx_data = txInfo }
);

// 3. Handle API response
var result = await response.Content.ReadFromJsonAsync<OrderResponse>();
```

## Thread Safety

- **Nonce Management**: Thread-safe using `lock()` on nonce increment
- **SignerClient**: Each instance manages its own state; safe for concurrent operations
- **Static Methods**: `GenerateApiKeyAsync()` is thread-safe

## Disposal

`SignerClient` implements `IDisposable`. Always use with `using`:

```csharp
using var signer = new SignerClient();
// ... use signer
```

Or manually dispose:

```csharp
var signer = new SignerClient();
try
{
    // ... use signer
}
finally
{
    signer.Dispose();
}
```

## Troubleshooting

### "Native library not found"
- Verify `signer-amd64.dll` or `.so` exists in `Native/` folder
- Check build output directory contains `Native/` folder
- Ensure correct platform binary is present

### "The current .NET SDK does not support targeting .NET 10.0"
- Install .NET 10.0 SDK from https://dotnet.microsoft.com/download
- Or change `<TargetFramework>` in `.csproj` to `net9.0`

### "Client not initialized"
- Call `InitializeAsync()` before any signing operations
- Check initialization didn't return an error

## References

- [Lighter Protocol Documentation](https://docs.lighter.xyz)
- [Python Implementation](https://github.com/elliottech/lighter-python)
- [Official Signing Library](https://github.com/elliottech/lighter-python/tree/main/lighter/signers)

## License

This wrapper is part of the GridBot project. Native libraries are provided by Lighter/Elliot Tech.
