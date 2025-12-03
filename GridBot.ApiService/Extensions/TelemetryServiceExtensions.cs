using GridBot.ApiService.Services.Telemetry;
using OpenTelemetry.Metrics;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering trading telemetry services.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Adds trading telemetry configuration to the service collection.
    /// Registers the custom trading meter with OpenTelemetry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTradingTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Add custom trading meter to OpenTelemetry metrics
        services.ConfigureOpenTelemetryMeterProvider(builder =>
        {
            builder.AddMeter(TradingMetrics.MeterName);
        });

        return services;
    }
}
