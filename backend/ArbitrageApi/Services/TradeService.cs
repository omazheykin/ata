using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Services;

public class TradeService : BackgroundService
{
    private readonly ILogger<TradeService> _logger;
    private bool _isAutoTradeEnabled = false;
    private decimal _minProfitThreshold = 0.5m; // 0.5% default
    private readonly ChannelProvider _channelProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> _hubContext;
    private readonly ArbitrageStatsService _statsService;
    private readonly RebalancingService _rebalancingService;
    private readonly OrderExecutionService _executionService;

    public TradeService(
        ILogger<TradeService> logger, 
        IConfiguration configuration, 
        StatePersistenceService persistenceService,
        Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> hubContext,
        ChannelProvider channelProvider,
        ArbitrageStatsService statsService,
        RebalancingService rebalancingService,
        OrderExecutionService executionService)
    {
        _logger = logger;
        _persistenceService = persistenceService;
        _hubContext = hubContext;
        _channelProvider = channelProvider;
        _statsService = statsService;
        _rebalancingService = rebalancingService;
        _executionService = executionService;
        
        // Load state from persistence
        var state = _persistenceService.GetState();
        _isAutoTradeEnabled = state.IsAutoTradeEnabled;
        _minProfitThreshold = state.MinProfitThreshold;
        _logger.LogInformation("Loaded state: AutoTrade={AutoTrade}, MinProfit={MinProfit}%", _isAutoTradeEnabled, _minProfitThreshold);

        var configStrategy = configuration.GetValue<string>("Trading:ExecutionStrategy");
        if (!string.IsNullOrEmpty(configStrategy) && Enum.TryParse<ExecutionStrategy>(configStrategy, true, out var strategy))
        {
            _executionService.SetExecutionStrategy(strategy);
            _logger.LogInformation("Loaded Execution Strategy from config: {Strategy}", strategy);
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Trade Service background worker started");

        var tradeTask = ProcessTradeSignalsAsync(stoppingToken);
        var rebalanceTask = ProcessRebalanceSignalsAsync(stoppingToken);

        await Task.WhenAll(tradeTask, rebalanceTask);
    }

    private async Task ProcessTradeSignalsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var opportunity in _channelProvider.TradeChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("💰 Trade Service: Signal received for {Symbol} ({Profit}%). Checking filters...", opportunity.Symbol, opportunity.ProfitPercentage);
                try
                {
                    var state = _persistenceService.GetState();
                    if (state.IsSafetyKillSwitchTriggered)
                    {
                        _logger.LogWarning("🛡️ Trade blocked by SAFETY KILL-SWITCH. Reason: {Reason}", state.GlobalKillSwitchReason);
                        continue;
                    }

                    if (!_isAutoTradeEnabled)
                    {
                        _logger.LogDebug("Auto-Trade is disabled, skipping opportunity for {Symbol}", opportunity.Symbol);
                        continue;
                    }

                    // CALENDAR INTEGRATION: Check activity zone before trading
                    var stats = await _statsService.GetStatsAsync();
                    var now = DateTime.UtcNow;
                    var day = now.DayOfWeek.ToString().Substring(0, 3);
                    var hour = now.Hour.ToString("D2");

                    if (stats.Calendar.TryGetValue(day, out var dayHours) && dayHours.TryGetValue(hour, out var detail))
                    {
                        if (detail.Zone == "low_activity")
                        {
                            _logger.LogInformation("📉 Auto-Trade: Current activity zone is LOW, but proceeding for verification... (Symbol: {Symbol})", opportunity.Symbol);
                            // continue;
                        }
                    }

                    // Determine base threshold (Global vs Pair-Specific)
                    var effectiveBaseThreshold = _minProfitThreshold;
                    if (state.PairThresholds.TryGetValue(opportunity.Symbol, out var pairThreshold))
                    {
                        effectiveBaseThreshold = pairThreshold;
                    }

                    // Note: Rebalancing adjustments are now handled by PassiveRebalancingService.
                    // This service handles STRICT profit-based trading.
                    // We simply respect the config threshold here.
                    
                    if (opportunity.ProfitPercentage < effectiveBaseThreshold)
                    {
                        _logger.LogInformation("🛑 Skipping {Symbol} ({Profit}%). Below threshold {Threshold}%", 
                            opportunity.Symbol, opportunity.ProfitPercentage, effectiveBaseThreshold);
                        continue;
                    }

                    await _executionService.ExecuteTradeAsync(opportunity, effectiveBaseThreshold, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing trade from channel for {Symbol}", opportunity.Symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Trade processing stopped.");
        }
    }

    private async Task ProcessRebalanceSignalsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var proposal in _channelProvider.RebalanceChannel.Reader.ReadAllAsync(stoppingToken))
            {
                var state = _persistenceService.GetState();
                if (!state.IsAutoRebalanceEnabled) continue;

                _logger.LogInformation("⚖️ [AUTO-REBALANCE] Signal received for {Asset}. Checking window and trend...", proposal.Asset);

                // Phase 3: Smart Decision
                var window = await _rebalancingService.GetTrendAnalysisService().GetBestWindowAsync(stoppingToken);
                
                bool isStrongTrend = !string.IsNullOrWhiteSpace(proposal.TrendDescription) && 
                                     !proposal.TrendDescription.Contains("Neutral", StringComparison.OrdinalIgnoreCase);
                bool isLowActivityWindow = window?.IsCurrent ?? false;

                if (isLowActivityWindow || isStrongTrend)
                {
                    _logger.LogInformation("🚀 [AUTO-REBALANCE] Criteria met: LowActivity={LowActivity}, Trend={Trend}. Executing...", 
                        isLowActivityWindow, proposal.TrendDescription);
                    await _rebalancingService.ExecuteRebalanceAsync(proposal, stoppingToken);
                }
                else
                {
                    _logger.LogInformation("⏳ [AUTO-REBALANCE] Waiting for better window or stronger trend. Current Window: {Day} {Hour}", 
                        DateTime.UtcNow.DayOfWeek, DateTime.UtcNow.Hour);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rebalance processing stopped.");
        }
    }

    public bool IsAutoTradeEnabled => _isAutoTradeEnabled;
    public decimal MinProfitThreshold => _minProfitThreshold;

    public void SetAutoTrade(bool enabled)
    {
        _isAutoTradeEnabled = enabled;
        _logger.LogInformation("Auto-Trade mode {Status}", enabled ? "ENABLED" : "DISABLED");
        
        var state = _persistenceService.GetState();
        state.IsAutoTradeEnabled = enabled;
        _persistenceService.SaveState(state);
    }

    public void SetMinProfitThreshold(decimal threshold)
    {
        _minProfitThreshold = threshold;
        _logger.LogInformation("Min Profit Threshold set to {Threshold}%", threshold);

        var state = _persistenceService.GetState();
        state.MinProfitThreshold = threshold;
        _persistenceService.SaveState(state);

        // Notify Detection Service via Channel
        _channelProvider.StrategyUpdateChannel.Writer.TryWrite(new StrategyUpdate
        {
            MinProfitThreshold = threshold,
            Reason = "Manual override (User Set)",
            Timestamp = DateTime.UtcNow
        });
    }
}
