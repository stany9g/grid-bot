using GridBot.Web;
using GridBot.Web.Components;
using GridBot.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
// Configure for Interactive WebAssembly (Auto mode)
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Add YARP for API proxying (browser -> web server -> apiservice)
builder.Services.AddHttpForwarder();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        client.BaseAddress = new("https+http://apiservice");
    });

// Configure webhook options (server-side only)
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));

// Register webhook notification service (server-side only)
builder.Services.AddHttpClient<IWebhookNotificationService, WebhookNotificationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

// API Proxy endpoint - forwards requests from browser to apiservice
// The WebAssembly client calls /api/* which gets proxied to the apiservice
app.MapForwarder("/api/{**catch-all}", "https+http://apiservice", "/api/{**catch-all}");

// Configure for Interactive WebAssembly (Auto mode)
// The client project contains the Dashboard and related components
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(GridBot.Web.Client._Imports).Assembly);

app.MapDefaultEndpoints();

app.Run();
