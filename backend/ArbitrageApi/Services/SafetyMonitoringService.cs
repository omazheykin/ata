using ArbitrageApi.Data;
using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services;

public class SafetyMonitoringService : BackgroundService
{
    private readonly ILogger<SafetyMonitoringService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ArbitrageDetectionService _detectionService;

    public SafetyMonitoringService(
        ILogger<SafetyMonitoringService> logger,
        IServiceProvider serviceProvider,
        StatePersistenceService persistenceService,
        IHubContext<ArbitrageHub> hubContext,
        ArbitrageDetectionService detectionService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _persistenceService = persistenceService;
        _hubContext = hubContext;
        _detectionService = detectionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🛡️ Safety Monitoring Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = _persistenceService.GetState();

                // If kill switch is already triggered, we just wait for user to reset it
                if (state.IsSafetyKillSwitchTriggered)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                // If auto-trade is OFF, there's nothing to monitor for safety
                if (!state.IsAutoTradeEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                await CheckSafetyLimitsAsync(state, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Safety Monitoring loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task CheckSafetyLimitsAsync(AppState state, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        // 1. Check Consecutive Losses
        var lastTransactions = await db.Transactions
            .OrderByDescending(t => t.Timestamp)
            .Take(state.MaxConsecutiveLosses)
            .ToListAsync(ct);

        if (lastTransactions.Count >= state.MaxConsecutiveLosses && 
            lastTransactions.All(t => t.Status == "Failed" || t.Status == "Partial"))
        {
            await TriggerKillSwitchAsync("Consecutive failures detected. Check API keys and network.", state);
            return;
        }

        // 2. Check 24h Drawdown
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var recentProfit = await db.Transactions
            .Where(t => t.Timestamp > cutoff && t.Status == "Success")
            .SumAsync(t => t.Profit, ct);

        // Profit is realized profit. If negative, it's a loss.
        if (recentProfit < -state.MaxDrawdownUsd)
        {
            await TriggerKillSwitchAsync($"Max daily drawdown reached (${Math.Abs(recentProfit):N2} > ${state.MaxDrawdownUsd:N2}).", state);
            return;
        }
        
        _logger.LogDebug("🛡️ Safety Check Passed. 24h PnL: ${PnL:N2}", recentProfit);
    }

    private async Task TriggerKillSwitchAsync(string reason, AppState state)
    {
        _logger.LogCritical("🚨 SAFETY KILL-SWITCH TRIGGERED: {Reason}", reason);

        state.IsAutoTradeEnabled = false;
        state.IsSafetyKillSwitchTriggered = true;
        state.GlobalKillSwitchReason = reason;

        _persistenceService.SaveState(state);

        // Broadcast to UI
        await _hubContext.Clients.All.SendAsync("ReceiveSafetyUpdate", new {
            isTriggered = true,
            reason = reason,
            timestamp = DateTime.UtcNow
        });

        // Also broadcast the trade disable update as it's a global setting change
        await _hubContext.Clients.All.SendAsync("ReceiveAutoTradeUpdate", false);
    }
}
