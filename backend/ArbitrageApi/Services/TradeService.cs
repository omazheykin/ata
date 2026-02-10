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
    private readonly ArbitrageStatsService _statsService; // Helper for stats, though maybe we don't need it for validation
    private readonly RebalancingService _rebalancingService;
    private readonly OrderExecutionService _executionService;
    private readonly ArbitrageCalculator _calculator;
    private readonly IEnumerable<IBookProvider> _bookProviders;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;

    public TradeService(
        ILogger<TradeService> logger, 
        IConfiguration configuration, 
        StatePersistenceService persistenceService,
        Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> hubContext,
        ChannelProvider channelProvider,
        ArbitrageStatsService statsService,
        RebalancingService rebalancingService,
        OrderExecutionService executionService,
        ArbitrageCalculator calculator,
        IEnumerable<IBookProvider> bookProviders,
        IEnumerable<IExchangeClient> exchangeClients)
    {
        _logger = logger;
        _persistenceService = persistenceService;
        _hubContext = hubContext;
        _channelProvider = channelProvider;
        _statsService = statsService;
        _rebalancingService = rebalancingService;
        _executionService = executionService;
        _calculator = calculator;
        _bookProviders = bookProviders;
        _exchangeClients = exchangeClients;
        
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
        _logger.LogInformation("🚀 Trade Service background worker started (Clean Architecture)");

        var tradeTask = ProcessTradeSignalsAsync(stoppingToken);
        var rebalanceTask = ProcessRebalanceSignalsAsync(stoppingToken);

        await Task.WhenAll(tradeTask, rebalanceTask);
    }

    private async Task ProcessTradeSignalsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var candidate in _channelProvider.TradeChannel.Reader.ReadAllAsync(stoppingToken))
            {
                // _logger.LogInformation("💰 Trade Signal received for {Symbol}. Validating...", candidate.Symbol);
                
                try
                {
                    var state = _persistenceService.GetState();
                    if (state.IsSafetyKillSwitchTriggered)
                    {
                        // _logger.LogWarning("🛡️ blocked by KILL-SWITCH.");
                        continue;
                    }

                    if (!_isAutoTradeEnabled) continue;

                    // 1. RE-VALIDATE with Full Data (OrderBook, Fees, Balances)
                    // The Detector only gave us a "Candidate" based on Top-of-Book and Calendar.
                    // We must now ensure it is ACTUALLY profitable with fees, slippage, and balances.

                    // A. Get Order Books
                    var buyBook = _bookProviders.FirstOrDefault(p => p.ExchangeName == candidate.BuyExchange)?.GetOrderBook(candidate.Symbol);
                    var sellBook = _bookProviders.FirstOrDefault(p => p.ExchangeName == candidate.SellExchange)?.GetOrderBook(candidate.Symbol);

                    if (buyBook == null || sellBook == null) 
                    {
                        _logger.LogWarning("⚠️ validation failed: Order books not available for {Symbol}", candidate.Symbol);
                        continue;
                    }

                    // B. Get Fees (Cached from Exchange Clients)
                    var buyClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == candidate.BuyExchange);
                    var sellClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == candidate.SellExchange);

                    if (buyClient == null || sellClient == null)
                    {
                        _logger.LogWarning("⚠️ validation failed: Exchange clients not found for {Symbol}", candidate.Symbol);
                        continue;
                    }

                    var buyFees = await buyClient.GetCachedFeesAsync() ?? (0.001m, 0.001m);
                    var sellFees = await sellClient.GetCachedFeesAsync() ?? (0.001m, 0.001m);

                    // C. Get Balances (Cached from Exchange Clients)
                    var buyBalances = await buyClient.GetCachedBalancesAsync();
                    var sellBalances = await sellClient.GetCachedBalancesAsync();

                    // 2. RUN CALCULATOR
                    var validatedOpp = _calculator.CalculatePairOpportunity(
                        candidate.Symbol,
                        candidate.BuyExchange,
                        candidate.SellExchange,
                        buyBook.Value.Asks, // We buy from Asks
                        sellBook.Value.Bids, // We sell to Bids
                        buyFees,
                        sellFees,
                        state.IsSandboxMode,
                        _minProfitThreshold, // Global threshold
                        buyBalances,
                        sellBalances,
                        state.SafeBalanceMultiplier,
                        state.UseTakerFees,
                        state.PairThresholds);

                    if (validatedOpp == null)
                    {
                        // _logger.LogInformation("❌ Validation failed for {Symbol} (Slippage/Fees/Balances)", candidate.Symbol);
                        continue;
                    }

                    // 3. CHECK PROFITABILITY logic was done inside Calculator (it returns null if < threshold)
                    // But we can double check or log.
                    
                    _logger.LogInformation("✅ Trade VALIDATED: {Symbol}, Net Profit: {NetProfit}%, Vol: {Vol}", 
                        validatedOpp.Symbol, validatedOpp.ProfitPercentage, validatedOpp.Volume);

                    // 4. EXECUTE
                    await _executionService.ExecuteTradeAsync(validatedOpp, _minProfitThreshold, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing trade validation for {Symbol}", candidate.Symbol);
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
