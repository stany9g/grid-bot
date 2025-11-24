# GridBot.Lighter

Complete C# client library for the [Lighter Protocol](https://lighter.xyz). Combines native transaction signing with REST API integration for end-to-end trading operations.

## Overview

This library provides:

### Native Transaction Signing
- **Platform Detection**: Automatically loads the correct native library for Windows (.dll) or Linux (.so)
- **Type-Safe API**: Strongly-typed request models with validation
- **Thread-Safe Nonce Management**: Automatic nonce handling for transaction signing

### REST API Client
- **Complete API Coverage**: Transaction, account, order, and market data endpoints
- **Simple HTTP Client**: Built on HttpClient with clean, readable code (no code generation)
- **Automatic Serialization**: Snake_case JSON handling with System.Text.Json

### Unified Client
- **High-Level Convenience**: `LighterClient` combines signing + API submission in single operations
- **Async/Await**: All operations return `Task<>` for non-blocking execution
- **Error Handling**: Clear exceptions with status codes and error messages

## Architecture

```
GridBot.Lighter/
├── Native/
│   ├── NativeMethods.cs       # P/Invoke declarations with platform detection
│   ├── NativeStructs.cs       # Marshaling structs for native interop
│   ├── signer-amd64.dll       # Windows native library
│   └── signer-amd64.so        # Linux native library
├── Models/
│   ├── Enums.cs               # OrderType, TimeInForce, MarginMode, etc.
│   ├── OrderRequest.cs        # Signing request models
│   ├── TransactionTypes.cs    # Transaction type constants (1-64)
│   └── Api/                   # REST API response models
│       ├── RespSendTx.cs
│       ├── Account.cs
│       ├── Order.cs
│       ├── OrderBook.cs
│       └── ...
├── Api/
│   └── LighterApiClient.cs    # REST API HTTP client
├── SignerClient.cs            # Native signing operations
└── LighterClient.cs           # ⭐ Unified client (signing + API)
```

## Configuration (Recommended)

The simplest approach is to configure Lighter settings in `appsettings.json`:

### 1. Add Configuration to appsettings.json

```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "PrivateKey": "your-api-private-key-here",
    "ChainId": 304,
    "ApiKeyIndex": 0,
    "AccountIndex": 123456,
    "InitialNonce": 0
  }
}
```

**IMPORTANT**: Never commit your private key to source control!

For development, use **User Secrets**:
```bash
dotnet user-secrets set "Lighter:PrivateKey" "your-private-key-here"
```

For production, use **environment variables** or **Azure Key Vault**.

### 2. Register in Program.cs

```csharp
using GridBot.Lighter;

var builder = WebApplication.CreateBuilder(args);

// Register Lighter client from configuration
builder.Services.AddLighterClient(builder.Configuration);

var app = builder.Build();
```

### 3. Inject and Use

```csharp
public class TradingService
{
    private readonly LighterClient _lighter;

    public TradingService(LighterClient lighter)
    {
        _lighter = lighter;
    }

    public async Task PlaceOrder()
    {
        var request = new CreateOrderRequest
        {
            MarketIndex = 0,
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BaseAmount = 1_000_000,
            Price = 50_000,
            IsAsk = false,
            OrderType = OrderType.Limit,
            TimeInForce = TimeInForce.GoodTillTime,
            OrderExpiry = OrderConstants.Default28DayOrderExpiry
        };

        var response = await _lighter.CreateOrderAsync(request);
        Console.WriteLine($"Order placed: {response.TxHash}");
    }
}
```

---

## Quick Start (Manual Initialization)

You can also initialize the client manually without dependency injection:

```csharp
using GridBot.Lighter;
using GridBot.Lighter.Models;

// 1. Initialize the unified client
using var client = new LighterClient();

await client.Signer.InitializeAsync(
    url: "https://mainnet.zklighter.elliot.ai",
    privateKey: "your-api-private-key",
    chainId: ChainId.Mainnet,
    apiKeyIndex: 0,
    accountIndex: 123456,
    initialNonce: 0
);

// 2. Create and submit an order in one call
var orderRequest = new CreateOrderRequest
{
    MarketIndex = 0,
    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    BaseAmount = 1_000_000,
    Price = 50_000,
    IsAsk = false,  // buy
    OrderType = OrderType.Limit,
    TimeInForce = TimeInForce.GoodTillTime,
    OrderExpiry = OrderConstants.Default28DayOrderExpiry
};

var response = await client.CreateOrderAsync(orderRequest);

Console.WriteLine($"Order submitted! Hash: {response.TxHash}");
Console.WriteLine($"Predicted execution: {response.PredictedExecutionTimeMs}ms");

// 3. Query your active orders
var activeOrders = await client.GetActiveOrdersAsync(123456);
Console.WriteLine($"Active orders: {activeOrders.Count}");

// 4. Get account information
var account = await client.GetAccountAsync(123456);
Console.WriteLine($"Collateral: {account.Collateral}");
Console.WriteLine($"Available: {account.AvailableBalance}");
```

## Supported Operations

### Transaction Operations (Signing + API Submission)
- ✅ Create single order (limit, market, stop-loss, take-profit)
- ✅ Create grouped orders (OCO, OTO, OTOCO)
- ✅ Cancel specific order
- ✅ Cancel all orders in a market
- ✅ Modify existing order
- ✅ Update position leverage
- ✅ Submit transaction batches

### Account & Order Queries
- ✅ Get account information (balances, positions)
- ✅ Get account metadata
- ✅ Get active orders
- ✅ Get transaction details

### Market Data Queries
- ✅ Get all order books (market metadata)
- ✅ Get order book details (bid/ask levels)

### Nonce Management
- ✅ Get next nonce from server
- ✅ Automatic nonce synchronization

### Future Operations (Native Library Supports)
- ⏳ Withdraw funds
- ⏳ Transfer between accounts
- ⏳ Create sub-accounts
- ⏳ Liquidity pool operations
- ⏳ Change public key
- ⏳ Create authentication token

## Usage Examples

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

## Advanced Usage

### Using Components Separately

If you need more control, you can use `SignerClient` and `LighterApiClient` separately:

```csharp
using GridBot.Lighter;
using GridBot.Lighter.Api;
using GridBot.Lighter.Models;

// Initialize signer
using var signer = new SignerClient();
await signer.InitializeAsync(/* ... */);

// Initialize API client
using var api = new LighterApiClient();

// Sign locally
var (txInfo, error) = await signer.CreateOrderAsync(request);
if (error != null)
{
    Console.WriteLine($"Signing failed: {error}");
    return;
}

// Submit to API
var response = await api.SendTransactionAsync(
    TransactionTypes.CreateOrder,
    txInfo!
);

Console.WriteLine($"Transaction hash: {response.TxHash}");
```

### Querying Market Data

```csharp
using var api = new LighterApiClient();

// Get all available markets
var orderBooks = await api.GetOrderBooksAsync();
foreach (var book in orderBooks)
{
    Console.WriteLine($"{book.Symbol} (ID: {book.MarketId})");
    Console.WriteLine($"  Maker fee: {book.MakerFee}");
    Console.WriteLine($"  Taker fee: {book.TakerFee}");
    Console.WriteLine($"  Min size: {book.MinBaseAmount}");
}

// Get order book depth for BTC-USDC (market ID 0)
var bookDetails = await api.GetOrderBookDetailsAsync(marketId: 0, depth: 10);
Console.WriteLine($"Best bid: {bookDetails.Bids.FirstOrDefault()?.Price}");
Console.WriteLine($"Best ask: {bookDetails.Asks.FirstOrDefault()?.Price}");
```

### Nonce Recovery

If you encounter nonce errors, sync with the server:

```csharp
using var client = new LighterClient();

try
{
    var response = await client.CreateOrderAsync(orderRequest);
}
catch (LighterApiException ex) when (ex.Message.Contains("nonce"))
{
    Console.WriteLine("Nonce mismatch, syncing with server...");

    var nonce = await client.SyncNonceAsync(
        accountIndex: 123456,
        apiKeyIndex: 0
    );

    Console.WriteLine($"Nonce synced to: {nonce}");

    // Retry the operation
    var response = await client.CreateOrderAsync(orderRequest);
}
```

### Custom Base URL (Testnet)

```csharp
// Connect to testnet
var apiClient = new LighterApiClient("https://testnet.zklighter.elliot.ai/api/v1/");
using var client = new LighterClient(apiClient);
```

## Thread Safety

- **Nonce Management**: Thread-safe using `lock()` on nonce increment
- **SignerClient**: Each instance manages its own state; safe for concurrent operations
- **Static Methods**: `GenerateApiKeyAsync()` is thread-safe

## Disposal

`LighterClient`, `SignerClient`, and `LighterApiClient` all implement `IDisposable`. Always use with `using`:

```csharp
// Unified client (recommended)
using var client = new LighterClient();
// ... use client

// Or individual components
using var signer = new SignerClient();
using var api = new LighterApiClient();
// ... use components
```

Or manually dispose:

```csharp
var client = new LighterClient();
try
{
    // ... use client
}
finally
{
    client.Dispose();
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
