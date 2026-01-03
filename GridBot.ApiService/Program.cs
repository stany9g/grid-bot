using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Trading;
using GridBot.ApiService.Components;
using GridBot.ApiService.Extensions;
using GridBot.ApiService.Services.Bot;
using GridBot.ApiService.Services.Exchange;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Network;
using GridBot.Core.Configuration;
using GridBot.Core.Services.Adaptive;
using GridBot.Core.Services.Configuration;
using GridBot.Extended;
using GridBot.Extended.Extensions;
using GridBot.Extended.Factory;
using GridBot.Lighter;
using GridBot.Lighter.Extensions;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Scalar.AspNetCore;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
        }

        builder.AddServiceDefaults();
        builder.AddRedisDistributedCache("cache");

        // Register Lighter network configuration (testnet/mainnet)
        // This registers the network factory and exchange registry.
        // The exchange client is created lazily when the network is selected.
        builder.Services.AddLighterNetworks(builder.Configuration);

        // Register Extended network configuration (testnet/mainnet)
        // This registers the network factory for runtime network switching.
        // The exchange client is created lazily when the network is selected.
        var extendedNetworksSection = builder.Configuration.GetSection(ExtendedNetworksOptions.SectionName);
        if (extendedNetworksSection.Exists())
        {
            builder.Services.AddExtendedNetworks(builder.Configuration);
            builder.Services.AddSingleton<IExtendedNetworkSelectionService, ExtendedNetworkSelectionService>();
        }

        builder.Services.AddTradingBot(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddMudServices();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();

        // Initialize the default Lighter network before starting the app
        // This creates the exchange client for the configured default network
        var lighterNetworkSelection = app.Services.GetRequiredService<INetworkSelectionService>();
        await lighterNetworkSelection.SelectNetworkAsync(lighterNetworkSelection.CurrentNetwork);

        // Initialize the default Extended network if configured
        var extendedNetworkSelection = app.Services.GetService<IExtendedNetworkSelectionService>();
        if (extendedNetworkSelection != null)
        {
            await extendedNetworkSelection.SelectNetworkAsync(extendedNetworkSelection.CurrentNetwork);
        }

        app.UseExceptionHandler();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Exchange abstraction endpoints (DEX-agnostic)
        var exchange = app.MapGroup("/api/exchange").WithTags("Exchange");

        exchange.MapGet("/markets", async (IMarketDataClient client, CancellationToken ct) =>
        {
            var result = await client.GetMarketsAsync(ct);
            return Results.Ok(result);
        }).WithName("GetExchangeMarkets");

        exchange.MapGet("/account", async (IAccountClient client, CancellationToken ct) =>
        {
            var result = await client.GetAccountAsync(ct);
            return Results.Ok(result);
        }).WithName("GetExchangeAccount");

        exchange.MapGet("/price/{marketId}", async (string marketId, IMarketDataClient client, CancellationToken ct) =>
        {
            var result = await client.GetCurrentPriceAsync(marketId, ct);
            return Results.Ok(new { MarketId = marketId, Price = result });
        }).WithName("GetExchangePrice");

        exchange.MapGet("/orderbook/{marketId}", async (string marketId, IMarketDataClient client, int depth = 20, CancellationToken ct = default) =>
        {
            var result = await client.GetOrderBookAsync(marketId, depth, ct);
            return Results.Ok(result);
        }).WithName("GetExchangeOrderBook");

        exchange.MapGet("/available", async (IExchangeSelectionService exchangeSelection, CancellationToken ct) =>
        {
            await exchangeSelection.RefreshAvailableExchangesAsync(ct);
            return Results.Ok(exchangeSelection.AvailableExchanges);
        }).WithName("GetAvailableExchanges");

        exchange.MapGet("/current", (IExchangeSelectionService exchangeSelection) =>
        {
            return Results.Ok(new
            {
                ExchangeType = exchangeSelection.CurrentExchangeType?.ToString(),
                ExchangeId = exchangeSelection.CurrentClient?.ExchangeId,
                IsConnected = exchangeSelection.CurrentClient?.Connection.IsHealthy ?? false
            });
        }).WithName("GetCurrentExchange");

        exchange.MapPost("/select", async (ExchangeSelectRequest request, IExchangeSelectionService exchangeSelection, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ExchangeType>(request.ExchangeType, ignoreCase: true, out var exchangeType))
            {
                return Results.BadRequest(new { Error = $"Invalid exchange type: {request.ExchangeType}" });
            }

            try
            {
                await exchangeSelection.SelectExchangeAsync(exchangeType, ct);
                return Results.Ok(new
                {
                    Success = true,
                    ExchangeType = exchangeSelection.CurrentExchangeType?.ToString(),
                    ExchangeId = exchangeSelection.CurrentClient?.ExchangeId
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("SelectExchange");

        // Network selection endpoints (testnet/mainnet)
        var network = app.MapGroup("/api/network").WithTags("Network");

        network.MapGet("/available", async (INetworkSelectionService networkSelection, CancellationToken ct) =>
        {
            await networkSelection.RefreshNetworkStatusAsync(ct);
            return Results.Ok(networkSelection.AvailableNetworks);
        }).WithName("GetAvailableNetworks");

        network.MapGet("/current", (INetworkSelectionService networkSelection) =>
        {
            return Results.Ok(new
            {
                NetworkType = networkSelection.CurrentNetwork.ToString(),
                DisplayName = GetNetworkDisplayName(networkSelection.CurrentNetwork)
            });
        }).WithName("GetCurrentNetwork");

        network.MapPost("/select", async (NetworkSelectRequest request, INetworkSelectionService networkSelection, CancellationToken ct) =>
        {
            if (!Enum.TryParse<LighterNetworkType>(request.NetworkType, ignoreCase: true, out var networkType))
            {
                return Results.BadRequest(new { Error = $"Invalid network type: {request.NetworkType}. Valid values are: Testnet, Mainnet" });
            }

            try
            {
                await networkSelection.SelectNetworkAsync(networkType, ct);
                return Results.Ok(new
                {
                    Success = true,
                    NetworkType = networkSelection.CurrentNetwork.ToString(),
                    DisplayName = GetNetworkDisplayName(networkSelection.CurrentNetwork)
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("SelectNetwork");

        // Legacy Lighter endpoints (deprecated, kept for backward compatibility)
        // Now uses abstraction interfaces instead of internal ILighterQueryClient
        var lighter = app.MapGroup("/api/lighter").WithTags("Lighter Trading (Deprecated)");

        lighter.MapGet("/markets", async (IMarketDataClient client, CancellationToken ct) =>
        {
            var result = await client.GetMarketsAsync(ct);
            return Results.Ok(result);
        }).WithName("GetLighterMarkets");

        lighter.MapGet("/account/{accountIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, IAccountClient client, CancellationToken ct) =>
        {
            // Note: accountIndex parameter is ignored - abstraction uses configured account
            var result = await client.GetAccountAsync(ct);
            return Results.Ok(result);
        }).WithName("GetLighterAccount");

        var trading = app.MapGroup("/api/trading").WithTags("Trading Dashboard");

        trading.MapGet("/status", (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, Microsoft.Extensions.Options.IOptions<GridBot.Core.Configuration.SimpleGridConfig> config) =>
        {
            var state = engine.State;
            var cfg = config.Value;
            return Results.Ok(new { TradingState = state.State.ToString(), IsRunning = engine.IsRunning, Market = cfg.Market });
        }).WithName("GetTradingStatus");

        trading.MapGet("/bot-status", (IGridBotControlService botControl, IExchangeSelectionService exchangeSelection, IOptions<SimpleGridConfig> config) =>
        {
            var cfg = config.Value;
            return Results.Ok(new
            {
                Status = botControl.Status.ToString(),
                IsRunning = botControl.IsRunning,
                CurrentExchangeId = botControl.CurrentExchangeId,
                CurrentExchangeType = botControl.CurrentExchangeType?.ToString(),
                LastError = botControl.LastError,
                Market = cfg.Market
            });
        }).WithName("GetBotStatus");

        trading.MapPost("/control/start", async (IGridBotControlService botControl, CancellationToken ct) =>
        {
            try
            {
                await botControl.StartAsync(ct);
                return Results.Ok(new { Success = true, Status = botControl.Status.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("StartTrading");

        trading.MapPost("/control/stop", async (IGridBotControlService botControl, CancellationToken ct) =>
        {
            try
            {
                await botControl.StopAsync(ct);
                return Results.Ok(new { Success = true, Status = botControl.Status.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("StopTrading");

        trading.MapPost("/control/pause", async (IGridBotControlService botControl, CancellationToken ct) =>
        {
            try
            {
                await botControl.PauseAsync("Manual pause", ct);
                return Results.Ok(new { Success = true, Status = botControl.Status.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("PauseTrading");

        trading.MapPost("/control/resume", async (IGridBotControlService botControl, CancellationToken ct) =>
        {
            try
            {
                await botControl.ResumeAsync(ct);
                return Results.Ok(new { Success = true, Status = botControl.Status.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Success = false, Error = ex.Message });
            }
        }).WithName("ResumeTrading");

        // Configuration endpoints
        var config = app.MapGroup("/api/config").WithTags("Configuration");

        config.MapGet("/", (IGridConfigurationService configService) =>
        {
            return Results.Ok(configService.Current);
        }).WithName("GetConfiguration");

        config.MapPut("/", async (RuntimeGridConfig incomingConfig, IGridConfigurationService configService, CancellationToken ct) =>
        {
            var errors = incomingConfig.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { Errors = errors });
            }

            await configService.UpdateAsync(c =>
            {
                // Grid Strategy (auto-tunable)
                c.GridSpacingPercent.Value = incomingConfig.GridSpacingPercent.Value;
                c.GridSpacingPercent.IsAuto = incomingConfig.GridSpacingPercent.IsAuto;
                c.BuyLevels.Value = incomingConfig.BuyLevels.Value;
                c.BuyLevels.IsAuto = incomingConfig.BuyLevels.IsAuto;
                c.SellLevels.Value = incomingConfig.SellLevels.Value;
                c.SellLevels.IsAuto = incomingConfig.SellLevels.IsAuto;
                c.OrderSizeUsdc.Value = incomingConfig.OrderSizeUsdc.Value;
                c.OrderSizeUsdc.IsAuto = incomingConfig.OrderSizeUsdc.IsAuto;

                // Risk configuration (fixed)
                c.MaxDailyLossPercent = incomingConfig.MaxDailyLossPercent;
                c.FlashCrashThresholdPercent = incomingConfig.FlashCrashThresholdPercent;
                c.PauseCooldownMinutes = incomingConfig.PauseCooldownMinutes;
                c.MaxPositionPercent = incomingConfig.MaxPositionPercent;

                // Exchange configuration (fixed)
                c.Market = incomingConfig.Market;
                c.MarketIndex = incomingConfig.MarketIndex;
                c.Leverage = incomingConfig.Leverage;

                // Timing configuration (fixed)
                c.LoopIntervalSeconds = incomingConfig.LoopIntervalSeconds;
                c.UsePostOnlyOrders = incomingConfig.UsePostOnlyOrders;
            }, ct);

            return Results.Ok(configService.Current);
        }).WithName("UpdateConfiguration");

        config.MapPost("/reset", async (IGridConfigurationService configService, CancellationToken ct) =>
        {
            await configService.ResetToDefaultsAsync(ct);
            return Results.Ok(configService.Current);
        }).WithName("ResetConfiguration");

        config.MapGet("/suggestions", async (
            IAdaptiveParameterService adaptiveService,
            IGridConfigurationService configService,
            IAccountClient accountClient,
            CancellationToken ct) =>
        {
            var account = await accountClient.GetAccountAsync(ct);
            var equity = account.PortfolioValue;

            var suggestions = await adaptiveService.CalculateSuggestionsAsync(
                configService.Current.MarketIndex,
                equity,
                ct);

            return Results.Ok(suggestions);
        }).WithName("GetSuggestions");

        config.MapGet("/markets", async (IMarketResolver marketResolver, CancellationToken ct) =>
        {
            var markets = await marketResolver.GetAvailableMarketsAsync(ct);
            return Results.Ok(markets);
        }).WithName("GetAvailableMarkets");

        config.MapPost("/apply-suggestions", async (
            IGridConfigurationService configService,
            IAdaptiveParameterService adaptiveService,
            IAccountClient accountClient,
            CancellationToken ct) =>
        {
            var account = await accountClient.GetAccountAsync(ct);
            var equity = account.PortfolioValue;

            var suggestions = await adaptiveService.CalculateSuggestionsAsync(
                configService.Current.MarketIndex,
                equity,
                ct);

            configService.UpdateSuggestions(
                suggestions.SuggestedSpacing,
                suggestions.SuggestedBuyLevels,
                suggestions.SuggestedSellLevels,
                suggestions.SuggestedOrderSize);

            await configService.SaveAsync(ct);
            return Results.Ok(configService.Current);
        }).WithName("ApplySuggestions");

        // Debug endpoints for Extended DEX order testing
        // These endpoints bypass abstractions for direct API testing
        var extendedDebug = app.MapGroup("/api/debug/extended").WithTags("Extended DEX Debug");

        extendedDebug.MapGet("/status", async (IServiceProvider sp, CancellationToken ct) =>
        {
            var factory = sp.GetService<IExtendedNetworkExchangeFactory>();
            if (factory == null)
            {
                return Results.BadRequest(new { Error = "Extended DEX not configured. Add ExtendedNetworks section to appsettings.json" });
            }

            var httpClient = factory.GetHttpClient();
            if (httpClient == null)
            {
                return Results.BadRequest(new
                {
                    Error = "Extended network not initialized. Select Extended network first.",
                    CurrentNetwork = factory.GetCurrentNetwork()?.ToString() ?? "None",
                    Hint = "Call POST /api/debug/extended/init to initialize"
                });
            }

            try
            {
                var accountTask = httpClient.GetAccountInfoAsync(ct);
                var balanceTask = httpClient.GetBalanceAsync(ct);
                var marketsTask = httpClient.GetMarketsAsync(ct);
                var ordersTask = httpClient.GetOrdersAsync(null, ct);

                await Task.WhenAll(accountTask, balanceTask, marketsTask, ordersTask);

                var btcMarket = (await marketsTask).FirstOrDefault(m =>
                    m.Name?.Contains("BTC", StringComparison.OrdinalIgnoreCase) == true);

                return Results.Ok(new
                {
                    Network = factory.GetCurrentNetwork()?.ToString(),
                    Account = await accountTask,
                    Balance = await balanceTask,
                    BtcMarket = btcMarket,
                    OpenOrders = await ordersTask,
                    AvailableMarkets = (await marketsTask).Select(m => m.Name).ToList()
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message, Type = ex.GetType().Name });
            }
        }).WithName("ExtendedDebugStatus");

        extendedDebug.MapPost("/init/{network}", async (string network, IServiceProvider sp, CancellationToken ct) =>
        {
            var factory = sp.GetService<IExtendedNetworkExchangeFactory>();
            if (factory == null)
            {
                return Results.BadRequest(new { Error = "Extended DEX not configured" });
            }

            if (!Enum.TryParse<ExtendedNetworkType>(network, ignoreCase: true, out var networkType))
            {
                return Results.BadRequest(new { Error = $"Invalid network: {network}. Use 'testnet' or 'mainnet'" });
            }

            try
            {
                await factory.CreateForNetworkAsync(networkType, ct);
                return Results.Ok(new
                {
                    Success = true,
                    Network = networkType.ToString(),
                    Message = $"Extended {networkType} initialized"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message, Type = ex.GetType().Name });
            }
        }).WithName("ExtendedDebugInit");

        extendedDebug.MapPost("/limit-order", async (IServiceProvider sp, CancellationToken ct) =>
        {
            // Hardcoded limit order: BUY minimum BTC
            // Minimum for BTC-USD is 0.0001 BTC (~$10 at $95k)
            const string market = "BTC-USD";
            const decimal quantity = 0.0001m;  // Minimum order size for BTC-USD (~$10 notional)
            const decimal price = 80000m;      // Below market - won't fill immediately
            const bool isBuy = true;

            var factory = sp.GetService<IExtendedNetworkExchangeFactory>();
            if (factory == null)
            {
                return Results.BadRequest(new { Error = "Extended DEX not configured" });
            }

            var httpClient = factory.GetHttpClient();
            var starkSigner = factory.GetStarkSigner();
            var options = factory.GetCurrentOptions();

            if (httpClient == null || starkSigner == null || options == null)
            {
                return Results.BadRequest(new { Error = "Extended network not initialized. Call POST /api/debug/extended/init first." });
            }

            try
            {
                // Get account info to retrieve vault ID (collateralPosition)
                var accountInfo = await httpClient.GetAccountInfoAsync(ct);
                if (string.IsNullOrEmpty(accountInfo.L2Vault))
                {
                    return Results.BadRequest(new { Error = "Account has no L2 vault configured" });
                }
                var positionId = long.Parse(accountInfo.L2Vault);

                // Get market info for L2Config (required for signing)
                var markets = await httpClient.GetMarketsAsync(ct);
                var marketInfo = markets.FirstOrDefault(m => m.Name == market);
                if (marketInfo?.L2Config == null)
                {
                    return Results.BadRequest(new { Error = $"Market {market} L2Config not found" });
                }
                var l2Config = marketInfo.L2Config;

                // Generate random nonce (32-bit) per Python SDK
                var nonce = StarkAmountCalculator.GenerateNonce();
                var expiryMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
                var clientOrderId = Guid.NewGuid().ToString("N");

                // Calculate settlement expiration (order expiry + 14 days, in seconds)
                var settlementExpiration = StarkAmountCalculator.CalcSettlementExpiration(expiryMs);

                // Calculate Stark amounts using market resolutions
                var (baseAmount, quoteAmount, feeAmount) = StarkAmountCalculator.CalculateStarkAmounts(
                    quantity,
                    price,
                    ExtendedConstants.DefaultFeeRate,
                    isBuy,
                    l2Config.SyntheticResolution,
                    l2Config.CollateralResolution);

                // Build StarkEx order parameters for signing
                var orderParams = new StarkExOrderParams
                {
                    PositionId = positionId,
                    BaseAmount = baseAmount,
                    QuoteAmount = quoteAmount,
                    FeeAmount = feeAmount,
                    Nonce = nonce,
                    ExpirationSeconds = settlementExpiration
                };

                // Sign the order using Extended DEX algorithm (includes domain params)
                var isTestnet = factory.GetCurrentNetwork() == ExtendedNetworkType.Testnet;
                var (r, s) = starkSigner.SignOrder(orderParams, l2Config, isTestnet);

                // Build the request with corrected structure per Python SDK
                var request = new GridBot.Extended.Models.Api.CreateOrderRequest
                {
                    Id = clientOrderId,
                    Market = market,
                    Type = "LIMIT",  // Uppercase required
                    Side = "BUY",
                    Qty = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Fee = ExtendedConstants.DefaultFeeRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ExpiryEpochMillis = expiryMs,
                    TimeInForce = "GTT",
                    ReduceOnly = false,
                    PostOnly = false,
                    Nonce = nonce.ToString(),  // At order root level, as string
                    SelfTradeProtectionLevel = "ACCOUNT",
                    Settlement = new GridBot.Extended.Models.Api.SettlementObject
                    {
                        StarkKey = starkSigner.StarkPublicKey ?? options.StarkPublicKey,
                        CollateralPosition = accountInfo.L2Vault,  // Vault ID
                        Signature = new GridBot.Extended.Models.Api.SignatureObject
                        {
                            R = r,
                            S = s
                        }
                    }
                };

                var response = await httpClient.CreateOrderAsync(request, ct);

                return Results.Ok(new
                {
                    Request = new
                    {
                        market,
                        side = "BUY",
                        Type = "LIMIT",
                        quantity,
                        price,
                        nonce,
                        clientOrderId,
                        vaultId = accountInfo.L2Vault,
                        baseAmount,
                        quoteAmount,
                        feeAmount,
                        settlementExpiration,
                        syntheticResolution = l2Config.SyntheticResolution,
                        collateralResolution = l2Config.CollateralResolution
                    },
                    Response = response
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message, Type = ex.GetType().Name, StackTrace = ex.StackTrace });
            }
        }).WithName("ExtendedDebugLimitOrder");

        extendedDebug.MapPost("/market-order", async (IServiceProvider sp, CancellationToken ct) =>
        {
            // Hardcoded market order: BUY minimum BTC
            // Extended uses LIMIT + IOC to simulate market orders
            // Minimum for BTC-USD is 0.0001 BTC (~$10 at $95k)
            const string market = "BTC-USD";
            const decimal quantity = 0.0001m;  // Minimum order size for BTC-USD (~$10 notional)
            const bool isBuy = true;

            var factory = sp.GetService<IExtendedNetworkExchangeFactory>();
            if (factory == null)
            {
                return Results.BadRequest(new { Error = "Extended DEX not configured" });
            }

            var httpClient = factory.GetHttpClient();
            var starkSigner = factory.GetStarkSigner();
            var options = factory.GetCurrentOptions();

            if (httpClient == null || starkSigner == null || options == null)
            {
                return Results.BadRequest(new { Error = "Extended network not initialized. Call POST /api/debug/extended/init first." });
            }

            try
            {
                // Get account info to retrieve vault ID (collateralPosition)
                var accountInfo = await httpClient.GetAccountInfoAsync(ct);
                if (string.IsNullOrEmpty(accountInfo.L2Vault))
                {
                    return Results.BadRequest(new { Error = "Account has no L2 vault configured" });
                }
                var positionId = long.Parse(accountInfo.L2Vault);

                // Get market info for L2Config (required for signing)
                var markets = await httpClient.GetMarketsAsync(ct);
                var marketInfo = markets.FirstOrDefault(m => m.Name == market);
                if (marketInfo?.L2Config == null)
                {
                    return Results.BadRequest(new { Error = $"Market {market} L2Config not found" });
                }
                var l2Config = marketInfo.L2Config;

                // Get current market price from order book
                var orderBook = await httpClient.GetOrderBookAsync(market, 5, ct);
                var bestAsk = orderBook.Asks?.FirstOrDefault();
                if (bestAsk == null)
                {
                    return Results.BadRequest(new { Error = "No asks in order book" });
                }

                // Parse best ask price and add 1% slippage for market buy
                if (!decimal.TryParse(bestAsk.Price, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var askPrice))
                {
                    return Results.BadRequest(new { Error = "Failed to parse ask price", RawPrice = bestAsk.Price });
                }
                var marketPrice = askPrice * 1.01m; // 1% above best ask for slippage

                // Generate random nonce (32-bit) per Python SDK
                var nonce = StarkAmountCalculator.GenerateNonce();
                var expiryMs = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(); // Short expiry for IOC
                var clientOrderId = Guid.NewGuid().ToString("N");

                // Calculate settlement expiration (order expiry + 14 days, in seconds)
                var settlementExpiration = StarkAmountCalculator.CalcSettlementExpiration(expiryMs);

                // Calculate Stark amounts using market resolutions
                var (baseAmount, quoteAmount, feeAmount) = StarkAmountCalculator.CalculateStarkAmounts(
                    quantity,
                    marketPrice,
                    ExtendedConstants.DefaultFeeRate,
                    isBuy,
                    l2Config.SyntheticResolution,
                    l2Config.CollateralResolution);

                // Build StarkEx order parameters for signing
                var orderParams = new StarkExOrderParams
                {
                    PositionId = positionId,
                    BaseAmount = baseAmount,
                    QuoteAmount = quoteAmount,
                    FeeAmount = feeAmount,
                    Nonce = nonce,
                    ExpirationSeconds = settlementExpiration
                };

                // Sign the order using Extended DEX algorithm (includes domain params)
                var isTestnet = factory.GetCurrentNetwork() == ExtendedNetworkType.Testnet;
                var (r, s) = starkSigner.SignOrder(orderParams, l2Config, isTestnet);

                // Build the request with corrected structure per Python SDK
                var request = new GridBot.Extended.Models.Api.CreateOrderRequest
                {
                    Id = clientOrderId,
                    Market = market,
                    Type = "LIMIT",  // Uppercase required
                    Side = "BUY",
                    Qty = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Price = marketPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Fee = ExtendedConstants.DefaultFeeRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ExpiryEpochMillis = expiryMs,
                    TimeInForce = "IOC",
                    ReduceOnly = false,
                    PostOnly = false,
                    Nonce = nonce.ToString(),  // At order root level, as string
                    SelfTradeProtectionLevel = "ACCOUNT",
                    Settlement = new GridBot.Extended.Models.Api.SettlementObject
                    {
                        StarkKey = starkSigner.StarkPublicKey ?? options.StarkPublicKey,
                        CollateralPosition = accountInfo.L2Vault,  // Vault ID
                        Signature = new GridBot.Extended.Models.Api.SignatureObject
                        {
                            R = r,
                            S = s
                        }
                    }
                };

                var response = await httpClient.CreateOrderAsync(request, ct);

                return Results.Ok(new
                {
                    OrderBookBestAsk = askPrice,
                    Request = new
                    {
                        market,
                        side = "BUY",
                        Type = "LIMIT (IOC)",
                        quantity,
                        price = marketPrice,
                        nonce,
                        clientOrderId,
                        vaultId = accountInfo.L2Vault,
                        baseAmount,
                        quoteAmount,
                        feeAmount,
                        settlementExpiration
                    },
                    Response = response
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message, Type = ex.GetType().Name, StackTrace = ex.StackTrace });
            }
        }).WithName("ExtendedDebugMarketOrder");

        extendedDebug.MapDelete("/cancel-all", async (IServiceProvider sp, CancellationToken ct) =>
        {
            const string market = "BTC-USD";

            var factory = sp.GetService<IExtendedNetworkExchangeFactory>();
            var httpClient = factory?.GetHttpClient();
            if (httpClient == null)
            {
                return Results.BadRequest(new { Error = "Extended network not initialized. Call POST /api/debug/extended/init first." });
            }

            try
            {
                var response = await httpClient.MassCancelOrdersAsync(
                    new GridBot.Extended.Models.Api.MassCancelRequest { Market = market }, ct);
                return Results.Ok(new { Market = market, Response = response });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message, Type = ex.GetType().Name });
            }
        }).WithName("ExtendedDebugCancelAll");

        app.MapDefaultEndpoints();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        await app.RunAsync();
    }

    private static string GetNetworkDisplayName(LighterNetworkType network) => network switch
    {
        LighterNetworkType.Testnet => "Lighter Testnet",
        LighterNetworkType.Mainnet => "Lighter Mainnet",
        _ => network.ToString()
    };
}