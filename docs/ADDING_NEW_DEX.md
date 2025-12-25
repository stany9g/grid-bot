# Adding a New DEX Integration

This guide explains how to add a new decentralized exchange (DEX) integration to GridBot using the abstraction layer.

## Prerequisites

- Familiarity with the target DEX's API documentation
- Understanding of WebSocket and REST API patterns
- Knowledge of the DEX's authentication/signing mechanism

## Architecture Overview

```
GridBot.Abstractions (DEX-agnostic interfaces)
    ↑ implements
GridBot.YourDex (your new project)
├── Adapters/
│   ├── YourDexExchangeClient.cs    → IExchangeClient
│   ├── YourDexOrderAdapter.cs      → IOrderClient
│   ├── YourDexAccountAdapter.cs    → IAccountClient
│   ├── YourDexMarketDataAdapter.cs → IMarketDataClient
│   ├── YourDexRealtimeAdapter.cs   → IRealtimeDataProvider
│   ├── YourDexScalingAdapter.cs    → IScalingProvider
│   ├── YourDexConnectionAdapter.cs → IExchangeConnection
│   └── YourDexAuthAdapter.cs       → IAuthenticationProvider
├── Extensions/
│   └── YourDexServiceExtensions.cs
└── YourDex.csproj
```

## Step-by-Step Guide

### Step 1: Create the Project

```bash
cd GridBot
dotnet new classlib -n GridBot.YourDex -f net10.0
dotnet sln GridBot.slnx add GridBot.YourDex/GridBot.YourDex.csproj
```

Add project reference to `GridBot.YourDex.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GridBot.Abstractions\GridBot.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0-*" />
  </ItemGroup>
</Project>
```

### Step 2: Create Configuration Options

```csharp
// YourDexOptions.cs
namespace GridBot.YourDex;

public sealed class YourDexOptions
{
    public const string SectionName = "YourDex";

    public required string ApiUrl { get; set; }
    public required string WebSocketUrl { get; set; }
    public required string PrivateKey { get; set; }
    public string? AccountId { get; set; }
    public bool DryRun { get; set; } = false;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl)) return "ApiUrl is required";
        if (string.IsNullOrWhiteSpace(PrivateKey)) return "PrivateKey is required";
        return null;
    }
}
```

### Step 3: Implement Core Adapters

#### 3.1 Market Data Adapter (IMarketDataClient)

```csharp
// Adapters/YourDexMarketDataAdapter.cs
using GridBot.Abstractions.Trading;
using GridBot.Abstractions.Models.Market;
using GridBot.Abstractions.Models.OrderBook;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexMarketDataAdapter : IMarketDataClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YourDexMarketDataAdapter> _logger;

    public YourDexMarketDataAdapter(
        HttpClient httpClient,
        ILogger<YourDexMarketDataAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<decimal> GetCurrentPriceAsync(string marketId, CancellationToken ct = default)
    {
        // Call your DEX API to get current price
        // Map response to decimal
        throw new NotImplementedException();
    }

    public async Task<OrderBookSnapshot> GetOrderBookAsync(string marketId, int depth = 20, CancellationToken ct = default)
    {
        // Call your DEX API to get order book
        // Map response to OrderBookSnapshot
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<CandlestickData>> GetCandlesticksAsync(
        string marketId, string resolution, int count, CancellationToken ct = default)
    {
        // Call your DEX API to get candlesticks
        // Map response to CandlestickData list
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default)
    {
        // Call your DEX API to get available markets
        // Map response to MarketInfo list
        throw new NotImplementedException();
    }

    public async Task<FundingRateInfo?> GetFundingRateAsync(string marketId, CancellationToken ct = default)
    {
        // Call your DEX API to get funding rate (if applicable)
        throw new NotImplementedException();
    }
}
```

#### 3.2 Order Adapter (IOrderClient)

```csharp
// Adapters/YourDexOrderAdapter.cs
using GridBot.Abstractions.Trading;
using GridBot.Abstractions.Models.Orders;
using GridBot.Abstractions.Scaling;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexOrderAdapter : IOrderClient
{
    private readonly HttpClient _httpClient;
    private readonly IScalingProvider _scaling;
    private readonly YourDexOptions _options;
    private readonly ILogger<YourDexOrderAdapter> _logger;

    public YourDexOrderAdapter(
        HttpClient httpClient,
        IScalingProvider scaling,
        IOptions<YourDexOptions> options,
        ILogger<YourDexOrderAdapter> logger)
    {
        _httpClient = httpClient;
        _scaling = scaling;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        try
        {
            // 1. Get market scaling
            var scaling = await _scaling.GetMarketScalingAsync(request.MarketId, ct);

            // 2. Scale price and amount to DEX format
            var scaledPrice = _scaling.ScalePrice(request.Price, scaling);
            var scaledAmount = _scaling.ScaleAmount(request.Size, scaling);

            // 3. Build DEX-specific request
            var dexRequest = new {
                market = request.MarketId,
                side = request.Side == OrderSide.Buy ? "buy" : "sell",
                price = scaledPrice,
                size = scaledAmount,
                // ... other DEX-specific fields
            };

            // 4. Sign and send request
            // 5. Parse response and return OrderResult

            throw new NotImplementedException();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order on YourDex");
            return OrderResult.Failure(ex.Message);
        }
    }

    public async Task<BatchOrderResult> CreateOrderBatchAsync(
        CreateOrderRequest[] requests, CancellationToken ct = default)
    {
        // Implement batch order creation if DEX supports it
        // Otherwise, loop through requests and call CreateOrderAsync
        throw new NotImplementedException();
    }

    public async Task<OrderResult> CancelOrderAsync(string marketId, string orderId, CancellationToken ct = default)
    {
        // Cancel specific order
        throw new NotImplementedException();
    }

    public async Task<OrderResult> CancelAllOrdersAsync(string marketId, CancellationToken ct = default)
    {
        // Cancel all orders for market
        throw new NotImplementedException();
    }

    public async Task<OrderResult> ModifyOrderAsync(ModifyOrderRequest request, CancellationToken ct = default)
    {
        // Modify existing order (if DEX supports it)
        throw new NotImplementedException();
    }
}
```

#### 3.3 Account Adapter (IAccountClient)

```csharp
// Adapters/YourDexAccountAdapter.cs
using GridBot.Abstractions.Trading;
using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Orders;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexAccountAdapter : IAccountClient
{
    private readonly HttpClient _httpClient;
    private readonly YourDexOptions _options;

    public async Task<AccountInfo> GetAccountAsync(CancellationToken ct = default)
    {
        // Fetch account info from DEX
        // Map to AccountInfo record
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(CancellationToken ct = default)
    {
        // Fetch positions from DEX
        // Map to PositionInfo list
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<OrderInfo>> GetActiveOrdersAsync(string marketId, CancellationToken ct = default)
    {
        // Fetch active orders from DEX
        // Map to OrderInfo list
        throw new NotImplementedException();
    }
}
```

#### 3.4 Scaling Adapter (IScalingProvider)

```csharp
// Adapters/YourDexScalingAdapter.cs
using GridBot.Abstractions.Scaling;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexScalingAdapter : IScalingProvider
{
    private readonly IMarketDataClient _marketData;
    private readonly ConcurrentDictionary<string, MarketScaling> _cache = new();

    public async Task<MarketScaling> GetMarketScalingAsync(string marketId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(marketId, out var cached))
            return cached;

        var markets = await _marketData.GetMarketsAsync(ct);
        var market = markets.FirstOrDefault(m => m.MarketId == marketId)
            ?? throw new InvalidOperationException($"Market {marketId} not found");

        var scaling = new MarketScaling
        {
            MarketId = marketId,
            Symbol = market.Symbol,
            PriceDecimals = market.PriceDecimals,
            SizeDecimals = market.SizeDecimals,
            MinOrderSize = market.MinOrderSize,
            LotSize = market.LotSize
        };

        _cache.TryAdd(marketId, scaling);
        return scaling;
    }

    public long ScalePrice(decimal price, MarketScaling scaling)
    {
        return (long)Math.Round(price * scaling.PriceMultiplier, MidpointRounding.AwayFromZero);
    }

    public long ScaleAmount(decimal amount, MarketScaling scaling)
    {
        return (long)Math.Round(amount * scaling.SizeMultiplier, MidpointRounding.AwayFromZero);
    }

    public decimal UnscalePrice(long scaledPrice, MarketScaling scaling)
    {
        return scaledPrice / scaling.PriceMultiplier;
    }

    public decimal UnscaleAmount(long scaledAmount, MarketScaling scaling)
    {
        return scaledAmount / scaling.SizeMultiplier;
    }
}
```

#### 3.5 Realtime Data Adapter (IRealtimeDataProvider)

```csharp
// Adapters/YourDexRealtimeAdapter.cs
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Models.OrderBook;
using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Orders;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexRealtimeAdapter : IRealtimeDataProvider
{
    private readonly YourDexWebSocketClient _wsClient;
    private readonly ConcurrentDictionary<string, OrderBookSnapshot> _orderBooks = new();
    private volatile AccountInfo? _account;

    public bool IsConnected => _wsClient.IsConnected;

    public OrderBookSnapshot? GetOrderBook(string marketId)
    {
        return _orderBooks.TryGetValue(marketId, out var snapshot) ? snapshot : null;
    }

    public AccountInfo? GetAccount() => _account;

    public IReadOnlyList<OrderInfo> GetOrders(string marketId)
    {
        // Return cached orders for market
        throw new NotImplementedException();
    }

    public decimal? GetCurrentPrice(string marketId)
    {
        var orderBook = GetOrderBook(marketId);
        if (orderBook is null) return null;
        return (orderBook.BestBidPrice + orderBook.BestAskPrice) / 2;
    }

    public PositionInfo? GetPosition(string marketId)
    {
        var account = GetAccount();
        return account?.Positions.TryGetValue(marketId, out var position) == true ? position : null;
    }

    public bool IsMarketDataReady(string marketId)
    {
        return _orderBooks.ContainsKey(marketId);
    }

    public async Task SubscribeMarketAsync(string marketId, CancellationToken ct = default)
    {
        await _wsClient.SubscribeOrderBookAsync(marketId, ct);
    }

    public async Task WaitForMarketDataAsync(string marketId, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!IsMarketDataReady(marketId))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timeout waiting for market data: {marketId}");
            await Task.Delay(100, ct);
        }
    }
}
```

#### 3.6 Connection Adapter (IExchangeConnection)

```csharp
// Adapters/YourDexConnectionAdapter.cs
using GridBot.Abstractions.Communication;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexConnectionAdapter : IExchangeConnection
{
    private readonly YourDexWebSocketClient _wsClient;
    private readonly string _exchangeId;

    public string ExchangeId => _exchangeId;
    public ConnectionState State => MapState(_wsClient.State);
    public bool IsHealthy => _wsClient.IsConnected;
    public TimeSpan? DataAge => _wsClient.TimeSinceLastMessage;
    public int DisconnectCount24h => _wsClient.DisconnectCount24h;

    public event EventHandler<ConnectionHealthEventArgs>? HealthChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _wsClient.ConnectAsync(ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _wsClient.DisconnectAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _wsClient.DisposeAsync();
    }

    private static ConnectionState MapState(YourDexConnectionState state) => state switch
    {
        YourDexConnectionState.Connected => ConnectionState.Connected,
        YourDexConnectionState.Connecting => ConnectionState.Connecting,
        YourDexConnectionState.Reconnecting => ConnectionState.Reconnecting,
        _ => ConnectionState.Disconnected
    };
}
```

#### 3.7 Auth Adapter (IAuthenticationProvider)

```csharp
// Adapters/YourDexAuthAdapter.cs
using GridBot.Abstractions.Authentication;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexAuthAdapter : IAuthenticationProvider
{
    private readonly YourDexSigner _signer;

    public async Task<AuthTokenResult> CreateAuthTokenAsync(int validitySeconds = 600, CancellationToken ct = default)
    {
        try
        {
            var token = await _signer.CreateAuthTokenAsync(validitySeconds);
            return new AuthTokenResult(token, null);
        }
        catch (Exception ex)
        {
            return new AuthTokenResult(null, ex.Message);
        }
    }

    public async Task<long> SyncNonceAsync(CancellationToken ct = default)
    {
        return await _signer.SyncNonceAsync(ct);
    }
}
```

#### 3.8 Exchange Client (IExchangeClient)

```csharp
// Adapters/YourDexExchangeClient.cs
using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Trading;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Authentication;

namespace GridBot.YourDex.Adapters;

internal sealed class YourDexExchangeClient : IExchangeClient
{
    public string ExchangeId { get; }
    public ExchangeType ExchangeType => ExchangeType.YourDex; // Add to enum
    public IExchangeConnection Connection { get; }
    public IRealtimeDataProvider RealtimeData { get; }
    public IOrderClient Orders { get; }
    public IAccountClient Account { get; }
    public IMarketDataClient MarketData { get; }
    public IScalingProvider Scaling { get; }
    public IAuthenticationProvider Auth { get; }
    public bool IsDryRunEnabled { get; }

    public YourDexExchangeClient(
        string exchangeId,
        IExchangeConnection connection,
        IRealtimeDataProvider realtimeData,
        IOrderClient orders,
        IAccountClient account,
        IMarketDataClient marketData,
        IScalingProvider scaling,
        IAuthenticationProvider auth,
        bool isDryRun)
    {
        ExchangeId = exchangeId;
        Connection = connection;
        RealtimeData = realtimeData;
        Orders = orders;
        Account = account;
        MarketData = marketData;
        Scaling = scaling;
        Auth = auth;
        IsDryRunEnabled = isDryRun;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}
```

### Step 4: Create Service Extension

```csharp
// Extensions/YourDexServiceExtensions.cs
using GridBot.Abstractions.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace GridBot.YourDex.Extensions;

public static class YourDexServiceExtensions
{
    public static IServiceCollection AddYourDexExchange(
        this IServiceCollection services,
        IConfiguration configuration,
        string exchangeId = "yourdex-main")
    {
        // 1. Bind configuration
        var options = new YourDexOptions();
        configuration.GetSection(YourDexOptions.SectionName).Bind(options);

        var validationError = options.Validate();
        if (validationError != null)
            throw new InvalidOperationException($"YourDex configuration error: {validationError}");

        services.Configure<YourDexOptions>(configuration.GetSection(YourDexOptions.SectionName));

        // 2. Register HTTP client
        services.AddHttpClient("YourDexClient", client =>
        {
            client.BaseAddress = new Uri(options.ApiUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 3. Register internal services (WebSocket client, signer, etc.)
        services.AddSingleton<YourDexWebSocketClient>();
        services.AddSingleton<YourDexSigner>();

        // 4. Register adapters
        services.AddSingleton<YourDexMarketDataAdapter>();
        services.AddSingleton<YourDexAccountAdapter>();
        services.AddSingleton<YourDexOrderAdapter>();
        services.AddSingleton<YourDexScalingAdapter>();
        services.AddSingleton<YourDexRealtimeAdapter>();
        services.AddSingleton<YourDexConnectionAdapter>();
        services.AddSingleton<YourDexAuthAdapter>();

        // 5. Register keyed IExchangeClient
        services.AddKeyedSingleton<IExchangeClient>(exchangeId, (sp, key) =>
        {
            var connection = sp.GetRequiredService<YourDexConnectionAdapter>();
            var realtime = sp.GetRequiredService<YourDexRealtimeAdapter>();
            var orders = sp.GetRequiredService<YourDexOrderAdapter>();
            var account = sp.GetRequiredService<YourDexAccountAdapter>();
            var marketData = sp.GetRequiredService<YourDexMarketDataAdapter>();
            var scaling = sp.GetRequiredService<YourDexScalingAdapter>();
            var auth = sp.GetRequiredService<YourDexAuthAdapter>();

            return new YourDexExchangeClient(
                exchangeId,
                connection,
                realtime,
                orders,
                account,
                marketData,
                scaling,
                auth,
                options.DryRun);
        });

        // 6. Register with IExchangeRegistry
        services.AddSingleton<IHostedService>(sp =>
        {
            var registry = sp.GetRequiredService<IExchangeRegistry>();
            var client = sp.GetRequiredKeyedService<IExchangeClient>(exchangeId);
            registry.Register(client);
            return new NoOpHostedService();
        });

        return services;
    }
}
```

### Step 5: Add ExchangeType Enum Value

Add your exchange to `GridBot.Abstractions/Factory/ExchangeType.cs`:

```csharp
public enum ExchangeType
{
    Lighter = 1,
    Hyperliquid = 2,
    YourDex = 3  // Add your exchange
}
```

### Step 6: Configure in appsettings.json

```json
{
  "Exchanges": {
    "Primary": "yourdex-main",
    "Instances": [
      {
        "Id": "yourdex-main",
        "Type": "YourDex",
        "DryRun": false
      }
    ]
  },
  "YourDex": {
    "ApiUrl": "https://api.yourdex.com",
    "WebSocketUrl": "wss://ws.yourdex.com",
    "PrivateKey": "your-private-key",
    "AccountId": "your-account-id"
  }
}
```

### Step 7: Register in Program.cs

```csharp
// Program.cs
using GridBot.YourDex.Extensions;

// Register your exchange
builder.Services.AddYourDexExchange(builder.Configuration, "yourdex-main");

// Or register multiple exchanges
builder.Services.AddLighterExchange(builder.Configuration, "lighter-main");
builder.Services.AddYourDexExchange(builder.Configuration, "yourdex-main");
```

## Testing Your Integration

### 1. Unit Tests

Create unit tests for each adapter:

```csharp
[Fact]
public async Task CreateOrder_ShouldScaleCorrectly()
{
    // Arrange
    var scaling = new MarketScaling { PriceDecimals = 2, SizeDecimals = 8 };
    var request = new CreateOrderRequest
    {
        MarketId = "BTC-USD",
        Price = 50000.50m,
        Size = 0.001m,
        Side = OrderSide.Buy
    };

    // Act
    var result = await _orderAdapter.CreateOrderAsync(request);

    // Assert
    Assert.True(result.IsSuccess);
}
```

### 2. Integration Tests with DryRun

```csharp
[Fact]
public async Task DryRun_ShouldNotSubmitRealOrders()
{
    // Configure with DryRun = true
    var services = new ServiceCollection();
    services.AddYourDexExchange(configuration, "yourdex-test");

    var provider = services.BuildServiceProvider();
    var client = provider.GetRequiredKeyedService<IExchangeClient>("yourdex-test");

    Assert.True(client.IsDryRunEnabled);
}
```

## Common Pitfalls

1. **Scaling Errors**: Always use `IScalingProvider` for price/amount conversion. Each DEX has different decimal precision.

2. **Rate Limiting**: Implement rate limiting in your HTTP client to avoid API bans.

3. **WebSocket Reconnection**: Handle disconnections gracefully with exponential backoff.

4. **Nonce Management**: If your DEX uses nonces, implement thread-safe nonce tracking.

5. **Error Mapping**: Map DEX-specific error codes to meaningful `OrderResult.Failure()` messages.

## Reference Implementation

See `GridBot.Lighter` for a complete reference implementation:

```
GridBot.Lighter/
├── Adapters/
│   ├── LighterExchangeClient.cs
│   ├── LighterOrderAdapter.cs
│   ├── LighterAccountAdapter.cs
│   ├── LighterMarketDataAdapter.cs
│   ├── LighterRealtimeAdapter.cs
│   ├── LighterScalingAdapter.cs
│   ├── LighterConnectionAdapter.cs
│   └── LighterAuthAdapter.cs
├── Extensions/
│   └── LighterAbstractionsExtensions.cs
└── ...
```
