using System.Net.Http.Json;

namespace GridBot.Web;

public sealed class TradingApiClient(HttpClient httpClient)
{
    public async Task<TradingStatusResponse?> GetStatusAsync(CancellationToken ct = default)
    {
        return await httpClient.GetFromJsonAsync<TradingStatusResponse>("/api/trading/status", ct);
    }

    public async Task<PositionSummaryResponse?> GetPositionAsync(int marketId, CancellationToken ct = default)
    {
        return await httpClient.GetFromJsonAsync<PositionSummaryResponse>($"/api/trading/position/{marketId}", ct);
    }

    public async Task<RiskIndicatorsResponse?> GetRiskAsync(int marketId, CancellationToken ct = default)
    {
        return await httpClient.GetFromJsonAsync<RiskIndicatorsResponse>($"/api/trading/risk/{marketId}", ct);
    }

    public async Task<ControlResponse?> PauseAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync("/api/trading/control/pause", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new ControlResponse(false, $"Server returned {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<ControlResponse>(ct);
    }

    public async Task<ControlResponse?> ResumeAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync("/api/trading/control/resume", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new ControlResponse(false, $"Server returned {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<ControlResponse>(ct);
    }

    public async Task<ControlResponse?> HaltAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync("/api/trading/control/halt", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new ControlResponse(false, $"Server returned {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<ControlResponse>(ct);
    }
}

/// <summary>
/// Response DTO for trading status endpoint.
/// </summary>
public sealed record TradingStatusResponse(
    string State,
    string TrendState,
    DateTimeOffset StateStartedAt,
    TimeSpan Uptime,
    int MarketId,
    string RecoveryPhase,
    decimal PositionMultiplier,
    decimal SpreadMultiplier,
    int ConsecutiveTimeouts
);

/// <summary>
/// Response DTO for position summary endpoint.
/// </summary>
public sealed record PositionSummaryResponse(
    int MarketId,
    string MoonBagState,
    decimal LockedQuantity,
    decimal HighWatermarkPrice,
    decimal TrailingStopPrice,
    decimal CurrentProfitPercent,
    decimal MaxPositionAchieved,
    bool HasActiveStopOrder
);

/// <summary>
/// Response DTO for risk indicators endpoint.
/// </summary>
public sealed record RiskIndicatorsResponse(
    int MarketId,
    decimal DailyPnlPercent,
    decimal WeeklyPnlPercent,
    decimal MonthlyPnlPercent,
    decimal DrawdownPercent,
    bool AnyLimitBreached,
    string? HaltReason,
    DateTimeOffset? HaltUntil,
    bool FlashCrashActive,
    string FlashCrashSeverity,
    string FlashCrashAction,
    DateTimeOffset? FlashCrashProtectionUntil,
    int CrashCount24h
);

/// <summary>
/// Response DTO for control endpoints (pause, resume, halt).
/// </summary>
public sealed record ControlResponse(bool Success, string Message);
