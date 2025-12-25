using GridBot.Core.Configuration;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.MarketData;

public sealed class MarketResolver : IMarketResolver
{
    private readonly ILighterQueryClient _queryClient;
    private readonly SimpleGridConfig _config;
    private readonly ILogger<MarketResolver> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private int _marketId;
    private string _resolvedSymbol = string.Empty;
    private bool _isInitialized;

    public MarketResolver(
        ILighterQueryClient queryClient,
        IOptions<SimpleGridConfig> config,
        ILogger<MarketResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _queryClient = queryClient;
        _config = config.Value;
        _logger = logger;
    }

    public int MarketId
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MarketResolver is not initialized. Call InitializeAsync first.");
            return _marketId;
        }
    }

    public string Symbol => _config.Market;

    public string ResolvedSymbol
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MarketResolver is not initialized. Call InitializeAsync first.");
            return _resolvedSymbol;
        }
    }

    public bool IsInitialized => _isInitialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            // Use MarketIndex from config directly
            _marketId = _config.MarketIndex;
            _resolvedSymbol = _config.Market;
            _isInitialized = true;

            _logger.LogInformation("Resolved market {Symbol} to ID {MarketId}", _resolvedSymbol, _marketId);
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}
