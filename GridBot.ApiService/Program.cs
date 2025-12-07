using GridBot.ApiService.Components;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Extensions;
using GridBot.ApiService.Models.Dashboard;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Dashboard;
using GridBot.ApiService.Services.DecisionEngine;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.MoonBag;
using GridBot.ApiService.Services.Realtime;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using GridBot.Lighter;
using MudBlazor.Services;
using Scalar.AspNetCore;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add service defaults & Aspire client integrations.
        builder.AddServiceDefaults();

        // Add trading telemetry (custom meters for OpenTelemetry)
        builder.Services.AddTradingTelemetry();

        // Add Redis distributed cache from Aspire
        builder.AddRedisDistributedCache("cache");

        // Add Lighter client from configuration
        builder.Services.AddLighterClient(builder.Configuration);

        // Add Lighter WebSocket client for real-time data streaming
        builder.Services.AddLighterWebSocket(builder.Configuration);

        // Register real-time state service (processes WebSocket channel events)
        builder.Services.AddSingleton<ILighterRealtimeState, LighterRealtimeStateService>();
        builder.Services.AddHostedService(sp =>
            (LighterRealtimeStateService)sp.GetRequiredService<ILighterRealtimeState>());

        // Add state persistence services
        builder.Services.AddPersistence();

        // Add ALTE trading bot services
        builder.Services.AddTradingBot(builder.Configuration);

        // Override IMarketDataService with HybridMarketDataService (WebSocket-first with REST fallback)
        // This must come after AddTradingBot which registers the default MarketDataService
        builder.Services.AddSingleton<IMarketDataService, HybridMarketDataService>();

        // Add services to the container.
        builder.Services.AddProblemDetails();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/opentapi
        builder.Services.AddOpenApi();

        // Add MudBlazor services
        builder.Services.AddMudServices();

        // Add Razor Components with Interactive Server mode
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Register dashboard state service as both a singleton and a hosted service
        builder.Services.AddSingleton<DashboardStateService>();
        builder.Services.AddSingleton<IDashboardStateService>(sp => sp.GetRequiredService<DashboardStateService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DashboardStateService>());

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler();

        // Add static file serving and antiforgery for Blazor
        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapOpenApi();
        app.MapScalarApiReference();


        var lighter = app.MapGroup("/api/lighter")
            .WithTags("Lighter Trading");

        // Order Management Endpoints
        lighter.MapPost("/orders", async (
            GridBot.Lighter.Models.CreateOrderRequest request,
            GridBot.Lighter.ILighterCommandClient client,
            IMarketScalingService scalingService,
            CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                // Validate lot size and minimum order size constraints
                var metadata = await scalingService.GetMarketMetadataAsync(request.MarketIndex, ct);
                var lotSize = metadata.LotSize;
                if (lotSize > 1 && request.BaseAmount % lotSize != 0)
                {
                    var snappedValue = (request.BaseAmount / lotSize) * lotSize;
                    var nextValid = snappedValue == 0 ? lotSize : snappedValue;
                    return Results.BadRequest(new
                    {
                        error = $"BaseAmount ({request.BaseAmount}) must be a multiple of the lot size ({lotSize}). " +
                                $"Valid values near your input: {nextValid}, {nextValid + lotSize}. " +
                                $"Market {metadata.Symbol} has SupportedSizeDecimals={metadata.SupportedSizeDecimals}, SizeDecimals={metadata.SizeDecimals}."
                    });
                }

                // Validate minimum order size
                var minBaseAmountScaled = (long)Math.Round(metadata.MinBaseAmount * metadata.SizeMultiplier);
                if (minBaseAmountScaled > 0 && request.BaseAmount < minBaseAmountScaled)
                {
                    return Results.BadRequest(new
                    {
                        error = $"BaseAmount ({request.BaseAmount}) is below minimum order size ({minBaseAmountScaled}). " +
                                $"Market {metadata.Symbol} requires at least {metadata.MinBaseAmount} base units ({minBaseAmountScaled} scaled)."
                    });
                }

                var result = await client.CreateOrderAsync(request, cancellationToken: ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CreateOrder")
        .WithSummary("Create a new order")
        .WithDescription("Creates and submits a limit, market, stop-loss, or take-profit order. BaseAmount must be a multiple of the market's lot size.");

        lighter.MapPost("/orders/market", async (
            GridBot.Lighter.Models.MarketOrderRequest request,
            GridBot.Lighter.ILighterCommandClient client,
            IMarketScalingService scalingService,
            CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                // Validate lot size and minimum order size constraints
                var metadata = await scalingService.GetMarketMetadataAsync(request.MarketIndex, ct);
                var lotSize = metadata.LotSize;
                if (lotSize > 1 && request.BaseAmount % lotSize != 0)
                {
                    var snappedValue = (request.BaseAmount / lotSize) * lotSize;
                    var nextValid = snappedValue == 0 ? lotSize : snappedValue;
                    return Results.BadRequest(new
                    {
                        error = $"BaseAmount ({request.BaseAmount}) must be a multiple of the lot size ({lotSize}). " +
                                $"Valid values near your input: {nextValid}, {nextValid + lotSize}. " +
                                $"Market {metadata.Symbol} has SupportedSizeDecimals={metadata.SupportedSizeDecimals}, SizeDecimals={metadata.SizeDecimals}."
                    });
                }

                // Validate minimum order size
                var minBaseAmountScaled = (long)Math.Round(metadata.MinBaseAmount * metadata.SizeMultiplier);
                if (minBaseAmountScaled > 0 && request.BaseAmount < minBaseAmountScaled)
                {
                    return Results.BadRequest(new
                    {
                        error = $"BaseAmount ({request.BaseAmount}) is below minimum order size ({minBaseAmountScaled}). " +
                                $"Market {metadata.Symbol} requires at least {metadata.MinBaseAmount} base units ({minBaseAmountScaled} scaled)."
                    });
                }

                var result = await client.CreateMarketOrderAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CreateMarketOrder")
        .WithSummary("Create a market order with slippage protection")
        .WithDescription("Creates and submits a market order. Automatically fetches current price from order book and applies slippage tolerance. BaseAmount must be a multiple of the market's lot size.");

        lighter.MapPost("/orders/grouped", async (
            GridBot.Lighter.Models.CreateGroupedOrdersRequest request,
            GridBot.Lighter.ILighterCommandClient client,
            IMarketScalingService scalingService,
            CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                // Pre-fetch all unique markets to avoid sequential API calls
                var uniqueMarketIds = request.Orders.Select(o => o.MarketIndex).Distinct();
                var metadataLookup = new Dictionary<int, MarketMetadata>();
                foreach (var marketId in uniqueMarketIds)
                {
                    metadataLookup[marketId] = await scalingService.GetMarketMetadataAsync(marketId, ct);
                }

                // Validate lot size and minimum order size for each order using cached metadata
                for (int i = 0; i < request.Orders.Count; i++)
                {
                    var order = request.Orders[i];
                    var metadata = metadataLookup[order.MarketIndex];
                    var lotSize = metadata.LotSize;
                    if (lotSize > 1 && order.BaseAmount % lotSize != 0)
                    {
                        var snappedValue = (order.BaseAmount / lotSize) * lotSize;
                        var nextValid = snappedValue == 0 ? lotSize : snappedValue;
                        return Results.BadRequest(new
                        {
                            error = $"Order {i}: BaseAmount ({order.BaseAmount}) must be a multiple of the lot size ({lotSize}). " +
                                    $"Valid values near your input: {nextValid}, {nextValid + lotSize}. " +
                                    $"Market {metadata.Symbol} has SupportedSizeDecimals={metadata.SupportedSizeDecimals}, SizeDecimals={metadata.SizeDecimals}."
                        });
                    }

                    // Validate minimum order size
                    var minBaseAmountScaled = (long)Math.Round(metadata.MinBaseAmount * metadata.SizeMultiplier);
                    if (minBaseAmountScaled > 0 && order.BaseAmount < minBaseAmountScaled)
                    {
                        return Results.BadRequest(new
                        {
                            error = $"Order {i}: BaseAmount ({order.BaseAmount}) is below minimum order size ({minBaseAmountScaled}). " +
                                    $"Market {metadata.Symbol} requires at least {metadata.MinBaseAmount} base units ({minBaseAmountScaled} scaled)."
                        });
                    }
                }

                var result = await client.CreateGroupedOrdersAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CreateGroupedOrders")
        .WithSummary("Create grouped orders")
        .WithDescription("Creates and submits grouped orders (OCO, OTO, OTOCO). BaseAmount for each order must be a multiple of the market's lot size.");

        lighter.MapDelete("/orders/{marketId}/{orderId}", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromRoute] long orderId, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.CancelOrderAsync(marketId, orderId, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CancelOrder")
        .WithSummary("Cancel a specific order")
        .WithDescription("Cancels a specific order by market ID and order ID.");

        lighter.MapDelete("/orders/{marketId}/cancel-all", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromQuery] long cancelTimestampMs, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.CancelAllOrdersAsync(marketId, cancelTimestampMs, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CancelAllOrders")
        .WithSummary("Cancel all orders in a market")
        .WithDescription("Cancels all orders in a specific market.");

        lighter.MapPut("/orders", async (
            GridBot.Lighter.Models.ModifyOrderRequest request,
            GridBot.Lighter.ILighterCommandClient client,
            IMarketScalingService scalingService,
            CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                // Validate lot size and minimum order size constraints for the new base amount
                var metadata = await scalingService.GetMarketMetadataAsync(request.MarketIndex, ct);
                var lotSize = metadata.LotSize;
                if (lotSize > 1 && request.NewBaseAmount % lotSize != 0)
                {
                    var snappedValue = (request.NewBaseAmount / lotSize) * lotSize;
                    var nextValid = snappedValue == 0 ? lotSize : snappedValue;
                    return Results.BadRequest(new
                    {
                        error = $"NewBaseAmount ({request.NewBaseAmount}) must be a multiple of the lot size ({lotSize}). " +
                                $"Valid values near your input: {nextValid}, {nextValid + lotSize}. " +
                                $"Market {metadata.Symbol} has SupportedSizeDecimals={metadata.SupportedSizeDecimals}, SizeDecimals={metadata.SizeDecimals}."
                    });
                }

                // Validate minimum order size
                var minBaseAmountScaled = (long)Math.Round(metadata.MinBaseAmount * metadata.SizeMultiplier);
                if (minBaseAmountScaled > 0 && request.NewBaseAmount < minBaseAmountScaled)
                {
                    return Results.BadRequest(new
                    {
                        error = $"NewBaseAmount ({request.NewBaseAmount}) is below minimum order size ({minBaseAmountScaled}). " +
                                $"Market {metadata.Symbol} requires at least {metadata.MinBaseAmount} base units ({minBaseAmountScaled} scaled)."
                    });
                }

                var result = await client.ModifyOrderAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("ModifyOrder")
        .WithSummary("Modify an existing order")
        .WithDescription("Modifies an existing order's price, size, or other parameters. NewBaseAmount must be a multiple of the market's lot size.");

        // Account Management Endpoints
        lighter.MapGet("/account/{accountIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetAccountAsync(accountIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetAccount")
        .WithSummary("Get account information")
        .WithDescription("Gets account information including positions and balances.");

        lighter.MapGet("/account/{accountIndex}/metadata", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetAccountMetadataAsync(accountIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetAccountMetadata")
        .WithSummary("Get account metadata")
        .WithDescription("Gets account metadata including public key and status.");

        lighter.MapGet("/account/{accountIndex}/market/{marketId}/orders", async (
            [Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex,
            [Microsoft.AspNetCore.Mvc.FromRoute] int marketId,
            GridBot.Lighter.ILighterQueryClient queryClient,
            GridBot.Lighter.ILighterCommandClient commandClient,
            CancellationToken ct) =>
        {
            try
            {
                // Generate auth token for authenticated API call
                var (authToken, authError) = await commandClient.CreateAuthTokenAsync();
                if (authError != null || string.IsNullOrEmpty(authToken))
                {
                    return Results.Problem(
                        detail: $"Failed to create auth token: {authError ?? "empty token"}",
                        statusCode: 500,
                        title: "Authentication Error");
                }

                var result = await queryClient.GetActiveOrdersAsync(accountIndex, marketId, authToken, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetActiveOrders")
        .WithSummary("Get active orders")
        .WithDescription("Gets active orders for an account on a specific market. Requires authentication.");

        // Market Data Endpoints
        lighter.MapGet("/markets", async (GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetOrderBooksAsync(ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetMarkets")
        .WithSummary("Get all markets")
        .WithDescription("Gets order book metadata for all markets.");

        lighter.MapGet("/markets/{marketId}/orderbook", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromQuery] int? depth, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetOrderBookDetailsAsync(marketId, depth, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetOrderBookDetails")
        .WithSummary("Get order book details")
        .WithDescription("Gets detailed order book data for a specific market.");

        // Transaction Endpoints
        lighter.MapGet("/transactions/{hashOrIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] string hashOrIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetTransactionAsync(hashOrIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetTransaction")
        .WithSummary("Get transaction details")
        .WithDescription("Gets a transaction by its hash or sequence index.");

        // Leverage Management Endpoints
        lighter.MapPut("/leverage", async (GridBot.Lighter.Models.UpdateLeverageRequest request, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                var result = await client.UpdateLeverageAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("UpdateLeverage")
        .WithSummary("Update position leverage")
        .WithDescription("Updates the leverage for a position in a specific market.");

        // Nonce Management Endpoints
        lighter.MapGet("/nonce", async ([Microsoft.AspNetCore.Mvc.FromQuery] long accountIndex, [Microsoft.AspNetCore.Mvc.FromQuery] int apiKeyIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetNextNonceAsync(accountIndex, apiKeyIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetNextNonce")
        .WithSummary("Get next nonce")
        .WithDescription("Gets the next nonce for an account from the server.");

        lighter.MapPost("/nonce/sync", async ([Microsoft.AspNetCore.Mvc.FromQuery] long accountIndex, [Microsoft.AspNetCore.Mvc.FromQuery] int apiKeyIndex, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.SyncNonceAsync(accountIndex, apiKeyIndex, ct);
                return Results.Ok(new { nonce = result });
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("SyncNonce")
        .WithSummary("Synchronize nonce")
        .WithDescription("Synchronizes the local signer nonce with the server.");

        // Trading Dashboard Endpoints
        var trading = app.MapGroup("/api/trading")
            .WithTags("Trading Dashboard");

        // Comprehensive dashboard endpoint - returns all data in one call
        trading.MapGet("/dashboard", async (
            ITradingStateService stateService,
            ITradingDecisionEngine decisionEngine,
            IMoonBagManager moonBagManager,
            ILossMonitor lossMonitor,
            IFlashCrashDetector flashCrashDetector,
            GridBot.ApiService.Services.Grid.IGridLifecycleService gridLifecycle,
            GridBot.ApiService.Services.MarketData.IMarketDataService marketDataService,
            GridBot.Lighter.ILighterQueryClient queryClient,
            Microsoft.Extensions.Options.IOptions<GridBot.Lighter.LighterOptions> lighterOptions,
            IRiskConfiguration riskConfig,
            CancellationToken ct) =>
        {
            try
            {
                var marketId = riskConfig.MarketId;
                var accountIndex = lighterOptions.Value.AccountIndex;

                // Gather all data in parallel
                var stateTask = Task.FromResult((
                    State: stateService.CurrentState,
                    TrendState: stateService.CurrentTrendState,
                    StateStartedAt: stateService.StateStartedAt
                ));

                var gridTask = gridLifecycle.GetCurrentGridStateAsync(marketId, ct);
                var moonBagTask = moonBagManager.GetMoonBagStatusAsync(marketId, ct);
                var lossTask = lossMonitor.GetCurrentLossStatusAsync(marketId, ct);
                var flashCrashTask = flashCrashDetector.CheckForFlashCrashAsync(marketId, ct);
                var priceTask = marketDataService.GetCurrentPriceAsync(marketId, ct);
                var accountTask = queryClient.GetAccountAsync(accountIndex, ct);

                await Task.WhenAll(gridTask, moonBagTask, lossTask, flashCrashTask, priceTask, accountTask);

                var stateInfo = await stateTask;
                var gridState = await gridTask;
                var moonBagStatus = await moonBagTask;
                var lossStatus = await lossTask;
                var flashCrashStatus = await flashCrashTask;
                var currentPrice = await priceTask;
                var account = await accountTask;

                // Get decision engine metrics
                var recoveryPhase = decisionEngine.GetCurrentRecoveryPhase(marketId);
                var positionMultiplier = decisionEngine.GetEffectivePositionMultiplier(marketId);
                var spreadMultiplier = decisionEngine.GetEffectiveSpreadMultiplier(marketId);
                var consecutiveTimeouts = decisionEngine.GetConsecutiveTimeoutCount(marketId);
                var lastDecisionResult = decisionEngine.GetLastDecisionResult(marketId);

                // Parse account data
                decimal.TryParse(account.Collateral, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var equity);
                decimal.TryParse(account.AvailableBalance, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var availableBalance);

                // Get position
                var position = account.Positions.FirstOrDefault(p => p.MarketId == marketId);
                decimal positionSize = 0;
                decimal unrealizedPnl = 0;
                if (position != null)
                {
                    decimal.TryParse(position.PositionSize, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out positionSize);
                    decimal.TryParse(position.UnrealizedPnl, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out unrealizedPnl);
                }

                // Calculate operational capacity
                var operationalCapacity = stateInfo.State switch
                {
                    TradingState.Active => 100,
                    TradingState.Degraded_Bootstrap => 50,
                    TradingState.Degraded_SkewCorrection => 75,
                    TradingState.Degraded_HighVolatility => 60,
                    TradingState.Degraded_LowLiquidity => 70,
                    TradingState.Degraded_ProtectiveMode => 10,
                    TradingState.Recovering => (int)(positionMultiplier * 100),
                    _ => 50
                };

                // Build grid state DTO
                GridBot.ApiService.Models.Dashboard.GridStateDto? gridDto = null;
                if (gridState != null)
                {
                    gridDto = new GridBot.ApiService.Models.Dashboard.GridStateDto
                    {
                        Status = gridState.Status.ToString(),
                        CenterPrice = gridState.Parameters?.CenterPrice ?? 0,
                        GridSpacing = gridState.Parameters?.GridSpacing ?? 0,
                        TotalWidth = gridState.Parameters?.TotalWidth ?? 0,
                        UpperBound = gridState.Parameters?.UpperBound ?? 0,
                        LowerBound = gridState.Parameters?.LowerBound ?? 0,
                        OrdersPerSide = gridState.Parameters?.OrdersPerSide ?? 0,
                        TotalFills = gridState.TotalFills,
                        ShiftCount = gridState.ShiftCount,
                        RebuildCount = gridState.RebuildCount,
                        ActiveOrders = gridState.Levels.Count(l => l.Status == GridBot.ApiService.Models.Trading.GridLevelStatus.Active),
                        Levels = gridState.Levels.Select(l => new GridBot.ApiService.Models.Dashboard.GridLevelDto
                        {
                            Price = l.Price,
                            IsBid = l.IsBid,
                            LevelIndex = l.LevelIndex,
                            Size = l.Size,
                            Status = l.Status.ToString()
                        }).ToList()
                    };
                }

                // Build trend info from last decision result
                GridBot.ApiService.Models.Dashboard.TrendInfoDto? trendDto = null;
                if (lastDecisionResult?.TrendResult != null)
                {
                    var trend = lastDecisionResult.TrendResult.TrendAnalysis;
                    var inventory = lastDecisionResult.TrendResult.InventoryAnalysis;
                    trendDto = new GridBot.ApiService.Models.Dashboard.TrendInfoDto
                    {
                        CurrentState = trend.CurrentState.ToString(),
                        ProposedState = trend.ProposedState.ToString(),
                        ConfirmationRequired = trend.ConfirmationRequired,
                        Ema20 = trend.Ema20,
                        Ema50 = trend.Ema50,
                        Adx = trend.Adx,
                        TargetSkew = trend.TargetSkew,
                        CurrentSkew = inventory.CurrentSkew,
                        RebalanceDelta = inventory.RebalanceDelta,
                        RebalanceNeeded = inventory.RebalanceNeeded,
                        CorrectionDirection = inventory.CorrectionDirection.ToString(),
                        InCooldown = trend.InCooldown
                    };
                }

                // Build decision cycle info
                GridBot.ApiService.Models.Dashboard.DecisionCycleDto? cycleDto = null;
                if (lastDecisionResult != null)
                {
                    cycleDto = new GridBot.ApiService.Models.Dashboard.DecisionCycleDto
                    {
                        LastCycleTime = lastDecisionResult.Timestamp,
                        CycleTimeMs = (int)lastDecisionResult.ExecutionDuration.TotalMilliseconds,
                        DataCollectionTimeMs = 0, // Not tracked separately
                        OrdersPlaced = lastDecisionResult.OrdersPlaced,
                        OrdersCancelled = lastDecisionResult.OrdersCancelled,
                        Success = lastDecisionResult.Success,
                        Warnings = lastDecisionResult.Warnings.ToList()
                    };
                }

                var response = new GridBot.ApiService.Models.Dashboard.DashboardResponse
                {
                    TradingState = stateInfo.State.ToString(),
                    StateStartedAt = stateInfo.StateStartedAt,
                    TrendState = stateInfo.TrendState.ToString(),
                    Uptime = DateTimeOffset.UtcNow - stateInfo.StateStartedAt,
                    MarketId = marketId,
                    RecoveryPhase = recoveryPhase.ToString(),
                    PositionMultiplier = positionMultiplier,
                    SpreadMultiplier = spreadMultiplier,
                    ConsecutiveTimeouts = consecutiveTimeouts,
                    OperationalCapacity = operationalCapacity,
                    CurrentPrice = currentPrice,
                    PositionSize = positionSize,
                    Equity = equity,
                    AvailableBalance = availableBalance,
                    UnrealizedPnl = unrealizedPnl,
                    UnrealizedPnlPercent = equity > 0 ? (unrealizedPnl / equity) * 100 : 0,
                    Grid = gridDto,
                    Risk = new GridBot.ApiService.Models.Dashboard.RiskInfoDto
                    {
                        TradingAllowed = !lossStatus.AnyLimitBreached,
                        BuysBlocked = flashCrashStatus.RequiredAction == GridBot.ApiService.Models.Trading.FlashCrashAction.PauseBuys ||
                                      flashCrashStatus.RequiredAction == GridBot.ApiService.Models.Trading.FlashCrashAction.PauseAll,
                        SellsBlocked = flashCrashStatus.RequiredAction == GridBot.ApiService.Models.Trading.FlashCrashAction.PauseAll,
                        Rolling24hPnlPercent = lossStatus.Rolling24hPnlPercent,
                        Rolling7dPnlPercent = lossStatus.Rolling7dPnlPercent,
                        Rolling30dPnlPercent = lossStatus.Rolling30dPnlPercent,
                        DrawdownPercent = lossStatus.DrawdownFromAthPercent,
                        TradesIn24h = lossStatus.TradesIn24h,
                        TradesIn7d = lossStatus.TradesIn7d,
                        AnyLimitBreached = lossStatus.AnyLimitBreached,
                        HaltReason = lossStatus.HaltReason,
                        HaltUntil = lossStatus.HaltUntil,
                        FlashCrashActive = flashCrashStatus.IsInProtection,
                        FlashCrashSeverity = flashCrashStatus.Severity.ToString(),
                        FlashCrashAction = flashCrashStatus.RequiredAction.ToString(),
                        FlashCrashProtectionUntil = flashCrashStatus.ProtectionUntil,
                        CrashCount24h = flashCrashDetector.GetCrashCount24h(marketId)
                    },
                    Trend = trendDto,
                    MoonBag = new GridBot.ApiService.Models.Dashboard.MoonBagInfoDto
                    {
                        State = moonBagStatus.State.ToString(),
                        LockedQuantity = moonBagStatus.LockedQuantity,
                        HighWatermarkPrice = moonBagStatus.HighWatermarkPrice,
                        TrailingStopPrice = moonBagStatus.TrailingStopPrice,
                        CurrentProfitPercent = moonBagStatus.CurrentProfitPercent,
                        MaxPositionAchieved = moonBagStatus.MaxPositionAchieved,
                        HasActiveStopOrder = moonBagStatus.HasActiveStopOrder
                    },
                    Cycle = cycleDto,
                    Timestamp = DateTimeOffset.UtcNow
                };

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Failed to get dashboard data");
            }
        })
        .WithName("GetDashboard")
        .WithSummary("Get comprehensive dashboard data")
        .WithDescription("Gets all dashboard data in a single call including trading state, grid, risk, trend, and moon bag status.");

        trading.MapGet("/status", (
            ITradingStateService stateService,
            ITradingDecisionEngine decisionEngine,
            IRiskConfiguration riskConfig) =>
        {
            try
            {
                var marketId = riskConfig.MarketId;
                var state = stateService.CurrentState;
                var trendState = stateService.CurrentTrendState;
                var stateStartedAt = stateService.StateStartedAt;
                var recoveryPhase = decisionEngine.GetCurrentRecoveryPhase(marketId);
                var positionMultiplier = decisionEngine.GetEffectivePositionMultiplier(marketId);
                var spreadMultiplier = decisionEngine.GetEffectiveSpreadMultiplier(marketId);
                var consecutiveTimeouts = decisionEngine.GetConsecutiveTimeoutCount(marketId);

                var response = new TradingStatusResponse(
                    State: state.ToString(),
                    TrendState: trendState.ToString(),
                    StateStartedAt: stateStartedAt,
                    Uptime: DateTimeOffset.UtcNow - stateStartedAt,
                    MarketId: marketId,
                    RecoveryPhase: recoveryPhase.ToString(),
                    PositionMultiplier: positionMultiplier,
                    SpreadMultiplier: spreadMultiplier,
                    ConsecutiveTimeouts: consecutiveTimeouts
                );

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Failed to get trading status");
            }
        })
        .WithName("GetTradingStatus")
        .WithSummary("Get trading status")
        .WithDescription("Gets the current trading status including state, trend, and recovery information.");

        trading.MapGet("/position/{marketId}", async (
            [Microsoft.AspNetCore.Mvc.FromRoute] int marketId,
            IMoonBagManager moonBagManager,
            CancellationToken ct) =>
        {
            try
            {
                var status = await moonBagManager.GetMoonBagStatusAsync(marketId, ct);

                var response = new PositionSummaryResponse(
                    MarketId: marketId,
                    MoonBagState: status.State.ToString(),
                    LockedQuantity: status.LockedQuantity,
                    HighWatermarkPrice: status.HighWatermarkPrice,
                    TrailingStopPrice: status.TrailingStopPrice,
                    CurrentProfitPercent: status.CurrentProfitPercent,
                    MaxPositionAchieved: status.MaxPositionAchieved,
                    HasActiveStopOrder: status.HasActiveStopOrder
                );

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Failed to get position summary");
            }
        })
        .WithName("GetPositionSummary")
        .WithSummary("Get position summary")
        .WithDescription("Gets the position and moon bag status for a specific market.");

        trading.MapGet("/risk/{marketId}", async (
            [Microsoft.AspNetCore.Mvc.FromRoute] int marketId,
            ILossMonitor lossMonitor,
            IFlashCrashDetector flashCrashDetector,
            CancellationToken ct) =>
        {
            try
            {
                var lossStatus = await lossMonitor.GetCurrentLossStatusAsync(marketId, ct);
                var flashCrashStatus = await flashCrashDetector.CheckForFlashCrashAsync(marketId, ct);
                var crashCount24h = flashCrashDetector.GetCrashCount24h(marketId);

                var response = new RiskIndicatorsResponse(
                    MarketId: marketId,
                    Rolling24hPnlPercent: lossStatus.Rolling24hPnlPercent,
                    Rolling7dPnlPercent: lossStatus.Rolling7dPnlPercent,
                    Rolling30dPnlPercent: lossStatus.Rolling30dPnlPercent,
                    DrawdownPercent: lossStatus.DrawdownFromAthPercent,
                    TradesIn24h: lossStatus.TradesIn24h,
                    TradesIn7d: lossStatus.TradesIn7d,
                    AnyLimitBreached: lossStatus.AnyLimitBreached,
                    HaltReason: lossStatus.HaltReason,
                    HaltUntil: lossStatus.HaltUntil,
                    FlashCrashActive: flashCrashStatus.IsInProtection,
                    FlashCrashSeverity: flashCrashStatus.Severity.ToString(),
                    FlashCrashAction: flashCrashStatus.RequiredAction.ToString(),
                    FlashCrashProtectionUntil: flashCrashStatus.ProtectionUntil,
                    CrashCount24h: crashCount24h
                );

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Failed to get risk indicators");
            }
        })
        .WithName("GetRiskIndicators")
        .WithSummary("Get risk indicators")
        .WithDescription("Gets the risk indicators including P&L, loss limits, and flash crash status.");

        trading.MapPost("/control/reduce-capacity", async (
            ITradingStateService stateService,
            CancellationToken ct) =>
        {
            try
            {
                // Enter protective mode - bot continues but at minimum capacity
                var success = await stateService.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Manual capacity reduction from dashboard");
                return success
                    ? Results.Ok(new ControlResponse(Success: true, Message: "Entered protective mode - trading at minimum capacity"))
                    : Results.BadRequest(new ControlResponse(Success: false, Message: "Failed to enter protective mode - invalid state transition"));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500, title: "Control operation failed");
            }
        })
        .WithName("ReduceCapacity")
        .WithSummary("Enter protective mode")
        .WithDescription("Reduces trading to minimum capacity. Bot continues monitoring and managing trailing stops but does not place new grid orders.");

        trading.MapPost("/control/resume", async (
            ITradingStateService stateService,
            CancellationToken ct) =>
        {
            try
            {
                // Enter recovery mode first, then will transition to Active automatically
                var success = await stateService.TransitionToAsync(TradingState.Recovering, "Manual recovery initiated from dashboard");
                return success
                    ? Results.Ok(new ControlResponse(Success: true, Message: "Recovery initiated - trading will gradually return to full capacity"))
                    : Results.BadRequest(new ControlResponse(Success: false, Message: "Failed to initiate recovery - invalid state transition"));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500, title: "Control operation failed");
            }
        })
        .WithName("ResumeTrading")
        .WithSummary("Initiate recovery")
        .WithDescription("Initiates recovery process to gradually return to full trading capacity.");

        trading.MapPost("/control/force-active", async (
            ITradingStateService stateService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await stateService.TransitionToAsync(TradingState.Active, "Force active from dashboard");
                return success
                    ? Results.Ok(new ControlResponse(Success: true, Message: "Trading at full capacity"))
                    : Results.BadRequest(new ControlResponse(Success: false, Message: "Failed to force active - invalid state transition"));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500, title: "Control operation failed");
            }
        })
        .WithName("ForceActive")
        .WithSummary("Force active state")
        .WithDescription("Forces immediate return to full active trading. Use with caution - bypasses recovery procedure.");

        app.MapDefaultEndpoints();

        // Map Razor components for Blazor Server dashboard
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Initialize market resolver before starting the application
        var marketResolver = app.Services.GetRequiredService<IMarketResolver>();
        await marketResolver.InitializeAsync();

        await app.RunAsync();
    }
}