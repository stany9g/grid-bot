using GridBot.Abstractions.Trading;
using GridBot.ApiService.Components;
using GridBot.ApiService.Extensions;
using GridBot.ApiService.Services.MarketData;
using GridBot.Core.Configuration;
using GridBot.Core.Services.Adaptive;
using GridBot.Core.Services.Configuration;
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

        // Register Lighter DEX with abstraction adapters
        builder.Services.AddLighterExchange(builder.Configuration, "lighter-main");

        builder.Services.AddTradingBot(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddMudServices();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();

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

        trading.MapPost("/control/pause", async (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, CancellationToken ct) =>
        {
            await engine.PauseAsync("Manual pause", ct);
            return Results.Ok(new { Success = true });
        }).WithName("PauseTrading");

        trading.MapPost("/control/resume", async (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, CancellationToken ct) =>
        {
            await engine.ResumeAsync(ct);
            return Results.Ok(new { Success = true });
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

        app.MapDefaultEndpoints();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        await app.RunAsync();
    }
}