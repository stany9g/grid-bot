namespace GridBot.ApiService.Services.Network;

/// <summary>
/// Hosted service that initializes the default network on application startup.
/// Runs early in the pipeline to ensure exchange client is available for other services.
/// </summary>
public sealed class NetworkInitializationService : IHostedService
{
    private readonly INetworkSelectionService _networkSelectionService;
    private readonly ILogger<NetworkInitializationService> _logger;

    public NetworkInitializationService(
        INetworkSelectionService networkSelectionService,
        ILogger<NetworkInitializationService> logger)
    {
        ArgumentNullException.ThrowIfNull(networkSelectionService);
        ArgumentNullException.ThrowIfNull(logger);

        _networkSelectionService = networkSelectionService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing default network...");

        try
        {
            var defaultNetwork = _networkSelectionService.CurrentNetwork;
            _logger.LogInformation("Selecting default network: {Network}", defaultNetwork);

            // This will create the exchange client for the default network
            await _networkSelectionService.SelectNetworkAsync(defaultNetwork, cancellationToken);

            _logger.LogInformation("Network initialization complete. Active network: {Network}", defaultNetwork);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize default network. Bot will not be able to start until a valid network is selected.");
            // Don't throw - let the app start anyway, user can select a different network
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Network initialization service stopping");
        return Task.CompletedTask;
    }
}
