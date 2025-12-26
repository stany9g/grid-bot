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