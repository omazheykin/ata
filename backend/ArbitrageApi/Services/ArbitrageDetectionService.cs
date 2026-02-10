using ArbitrageApi.Models;
using ArbitrageApi.Hubs;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Services.Strategies;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Services;

public class ArbitrageDetectionService : BackgroundService
{
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ILogger<ArbitrageDetectionService> _logger;
    private readonly IEnumerable<IBookProvider> _bookProviders;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly ArbitrageCalculator _calculator;
    private readonly ChannelProvider _channelProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly SmartStrategyService _smartStrategyService;
    private readonly List<ArbitrageOpportunity> _recentOpportunities = new();
    private readonly object _lock = new();
    private bool _isSandboxMode = false;
    private decimal _currentMinProfitThreshold = 0.1m;
    private string _currentStrategyReason = "Initial startup (default)";

    public bool IsSandboxMode => _isSandboxMode;
    public bool IsSmartStrategyEnabled => _persistenceService.GetState().IsSmartStrategyEnabled;

    public (decimal Threshold, string Reason) GetCurrentStrategy()
    {
        return (_currentMinProfitThreshold, _currentStrategyReason);
    }

    public ArbitrageDetectionService(
        IHubContext<ArbitrageHub> hubContext,
        ILogger<ArbitrageDetectionService> logger,
        IEnumerable<IBookProvider> bookProviders,
        IEnumerable<IExchangeClient> exchangeClients,
        ChannelProvider channelProvider,
        ArbitrageCalculator calculator,
        StatePersistenceService persistenceService,
        SmartStrategyService smartStrategyService)
    {
        _hubContext = hubContext;
        _logger = logger;
        _bookProviders = bookProviders;
        _exchangeClients = exchangeClients;
        _channelProvider = channelProvider;
        _calculator = calculator;
        _persistenceService = persistenceService;
        _smartStrategyService = smartStrategyService;

        // Load state from persistence
        var state = _persistenceService.GetState();
        _isSandboxMode = state.IsSandboxMode;
        _currentMinProfitThreshold = state.MinProfitThreshold;
        if (!state.IsSmartStrategyEnabled)
        {
            _currentStrategyReason = "Manual Mode (using settings)";
        }

        // PROPAGATE SANDBOX MODE TO CLIENTS
        foreach (var client in _exchangeClients)
        {
            try
            {
                client.SetSandboxMode(_isSandboxMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing sandbox mode for {Exchange}", client.ExchangeName);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Arbitrage Detection Service started (Event-Driven) in {Mode} mode", _isSandboxMode ? "SANDBOX" : "REAL");

        try
        {
            var strategyTask = ListenForStrategyUpdatesAsync(stoppingToken);
            var detectionTask = RunDetectionLoopAsync(stoppingToken);
            var broadcastTask = PeriodicallyBroadcastStatusAsync(stoppingToken);

            await Task.WhenAll(strategyTask, detectionTask, broadcastTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Arbitrage Detection Service stopping...");
        }
    }

    private async Task ListenForStrategyUpdatesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📡 Detection Service: Listening for Strategy Updates...");
        await foreach (var update in _channelProvider.StrategyUpdateChannel.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogWarning("🎯 STRATEGY UPDATE RECEIVED: New Threshold = {Threshold}%. Reason: {Reason}", 
                update.MinProfitThreshold, update.Reason);
            
            _currentMinProfitThreshold = update.MinProfitThreshold;
            _currentStrategyReason = update.Reason;

            await _hubContext.Clients.All.SendAsync("ReceiveStrategyUpdate", new { 
                threshold = _currentMinProfitThreshold, 
                reason = _currentStrategyReason 
            }, stoppingToken);
        }
    }

    private async Task RunDetectionLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔍 Detection Loop starting (Multi-Pair Sweep)...");
        
        await foreach (var symbol in _channelProvider.MarketUpdateChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>();
                var feesDict = new Dictionary<string, (decimal Maker, decimal Taker)>();
                
                foreach (var provider in _bookProviders)
                {
                    var book = provider.GetOrderBook(symbol);
                    if (book != null)
                    {
                        orderBooks[provider.ExchangeName] = book.Value;
                        feesDict[provider.ExchangeName] = await provider.GetCachedFeesAsync() ?? (0.001m, 0.001m);
                    }
                }

                // 3. Broadcast Latest Prices for Comparison Chart
                if (orderBooks.Count > 0)
                {
                    var priceUpdate = new
                    {
                        asset = symbol,
                        prices = orderBooks.ToDictionary(
                            kvp => kvp.Key, // Exchange Name
                            kvp => kvp.Value.Asks.Count > 0 ? kvp.Value.Asks[0].Price : (kvp.Value.Bids.Count > 0 ? kvp.Value.Bids[0].Price : 0m)
                        ),
                        timestamp = DateTime.UtcNow
                    };
                    await _hubContext.Clients.All.SendAsync("ReceiveMarketPrices", priceUpdate, stoppingToken);
                }

                if (orderBooks.Count < 2) continue;

                var exchanges = orderBooks.Keys.ToList();
                for (int i = 0; i < exchanges.Count; i++)
                {
                    for (int j = 0; j < exchanges.Count; j++)
                    {
                        if (i == j) continue;

                        string buyEx = exchanges[i];
                        string sellEx = exchanges[j];

                        var state = _persistenceService.GetState();
                        var opportunity = _calculator.CalculatePairOpportunity(
                            symbol,
                            buyEx,
                            sellEx,
                            orderBooks[buyEx].Asks,
                            orderBooks[sellEx].Bids,
                            feesDict[buyEx],
                            feesDict[sellEx],
                            _isSandboxMode,
                            _currentMinProfitThreshold,
                            null, // Balances will be added later or fetched from a service
                            state.SafeBalanceMultiplier,
                            state.UseTakerFees,
                            state.PairThresholds
                        );

                        if (opportunity != null)
                        {
                            await ProcessOpportunityAsync(opportunity, symbol, stoppingToken);
                        }
                    }
                }
                
                await Task.Delay(50, stoppingToken); // Small delay between symbols
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing market update for {Symbol}", symbol);
            }
        }
    }

    private async Task ProcessOpportunityAsync(ArbitrageOpportunity opportunity, string symbol, CancellationToken stoppingToken)
    {
        // 1. Statistics tracking (Volatility & Spread)
        // Log even slightly negative spreads to show market activity on the heatmap
        if (opportunity.ProfitPercentage > -0.5m && opportunity.ProfitPercentage <= 10.0m)
        {
            await _channelProvider.EventChannel.Writer.WriteAsync(new ArbitrageEvent
            {
                Id = Guid.NewGuid(),
                Pair = symbol,
                Direction = $"{opportunity.BuyExchange.Substring(0, 1)}→{opportunity.SellExchange.Substring(0, 1)}",
                Spread = opportunity.ProfitPercentage / 100,
                SpreadPercent = opportunity.ProfitPercentage,
                DepthBuy = opportunity.BuyDepth,
                DepthSell = opportunity.SellDepth,
                Timestamp = DateTime.UtcNow
            }, stoppingToken);
        }

        // 2. Dashboard and Trading
        
        // Calculate total value of the opportunity in USD (Volume * Price)
        // Ensure strictly > 0 profit and meets minimum value threshold ($10 for now)
        bool isValuableEnough = (opportunity.Volume * opportunity.BuyPrice) >= 10m;
        
        // In sandbox mode, allow even smaller opportunities for testing
        if (_isSandboxMode) isValuableEnough = true;

        if (opportunity.ProfitPercentage >= _currentMinProfitThreshold 
            && (opportunity.ProfitPercentage > 0 || _isSandboxMode && opportunity.ProfitPercentage > -0.5m)
            && isValuableEnough)
        {
            lock (_lock)
            {
                // Check if we already have a similar opportunity to avoid spamming the same one
                var existing = _recentOpportunities.FirstOrDefault(o => 
                    o.Symbol == opportunity.Symbol && 
                    o.BuyExchange == opportunity.BuyExchange && 
                    o.SellExchange == opportunity.SellExchange);
                
                if (existing != null)
                {
                    _recentOpportunities.Remove(existing);
                }

                _recentOpportunities.Add(opportunity);
                if (_recentOpportunities.Count > 100) _recentOpportunities.RemoveAt(0);
            }
            
            await _hubContext.Clients.All.SendAsync("ReceiveOpportunity", opportunity, stoppingToken);
            
            // Determine effective threshold for this pair
            var effectiveThreshold = _currentMinProfitThreshold;
            var state = _persistenceService.GetState();
            if (state.PairThresholds.TryGetValue(symbol, out var pairLimit))
            {
                effectiveThreshold = pairLimit;
            }

            // Forward to Trade Service if profitable (TradeService will apply smart filtering/rebalancing logic)
            if (opportunity.ProfitPercentage >= effectiveThreshold)
            {
                await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity, stoppingToken);
            }
            else if (opportunity.ProfitPercentage >= 0.01m)
            {
                // Low profit, but potentially useful for rebalancing
                await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(opportunity, stoppingToken);
            }
        }
    }

    public List<ArbitrageOpportunity> GetRecentOpportunities()
    {
        lock (_lock)
        {
            return _recentOpportunities.ToList();
        }
    }

    public async Task SetSandboxMode(bool enabled)
    {
        _logger.LogInformation("🔄 Switching Global Sandbox Mode to: {Status}", enabled ? "ENABLED" : "DISABLED");
        _isSandboxMode = enabled;
        
        var state = _persistenceService.GetState();
        state.IsSandboxMode = enabled;
        _persistenceService.SaveState(state);

        foreach (var client in _exchangeClients)
        {
            try 
            {
                client.SetSandboxMode(enabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating sandbox mode for client {ExchangeName}", client.ExchangeName);
            }
        }
        
        await _hubContext.Clients.All.SendAsync("ReceiveSandboxModeUpdate", enabled);
    }

    public async Task SetSmartStrategy(bool enabled)
    {
        _logger.LogInformation("🔄 Switching Smart Strategy to: {Status}", enabled ? "ENABLED" : "DISABLED");
        
        var state = _persistenceService.GetState();
        state.IsSmartStrategyEnabled = enabled;
        _persistenceService.SaveState(state);

        await _hubContext.Clients.All.SendAsync("ReceiveSmartStrategyUpdate", enabled);
        
        if (!enabled)
        {
            _currentMinProfitThreshold = state.MinProfitThreshold;
            _currentStrategyReason = "Manual Mode (using settings)";
            
            await _hubContext.Clients.All.SendAsync("ReceiveStrategyUpdate", new { 
                threshold = _currentMinProfitThreshold, 
                reason = _currentStrategyReason 
            });
        }
        else
        {
            _currentStrategyReason = "Smart Mode Activated (Analyzing market data...)";
            
            // Broadcast the intent immediately
            await _hubContext.Clients.All.SendAsync("ReceiveStrategyUpdate", new { 
                threshold = _currentMinProfitThreshold, 
                reason = _currentStrategyReason 
            });

            // Trigger the Stats service to recalculate RIGHT NOW
            _smartStrategyService.TriggerUpdate();
        }
    }

    public async Task SetPairThresholds(Dictionary<string, decimal> thresholds)
    {
        _logger.LogInformation("🔄 Updating Pair Thresholds: {Count} pairs", thresholds.Count);
        var state = _persistenceService.GetState();
        state.PairThresholds = thresholds;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceivePairThresholdsUpdate", thresholds);
    }

    public async Task SetSafeBalanceMultiplier(decimal multiplier)
    {
        _logger.LogInformation("🔄 Updating Safe Balance Multiplier to: {Multiplier}", multiplier);
        var state = _persistenceService.GetState();
        state.SafeBalanceMultiplier = multiplier;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveSafeBalanceMultiplierUpdate", multiplier);
    }

    public async Task SetUseTakerFees(bool useTakerFees)
    {
        _logger.LogInformation("🔄 Updating Fee Mode: {Mode}", useTakerFees ? "Taker (Pessimistic)" : "Maker (Optimistic)");
        var state = _persistenceService.GetState();
        state.UseTakerFees = useTakerFees;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveFeeModeUpdate", useTakerFees);
    }

    public async Task SetAutoRebalance(bool enabled)
    {
        _logger.LogInformation("🔄 Switching Auto-Rebalance to: {Status}", enabled ? "ENABLED" : "DISABLED");
        var state = _persistenceService.GetState();
        state.IsAutoRebalanceEnabled = enabled;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveAutoRebalanceUpdate", enabled);
    }

    public async Task ResetSafetyKillSwitch()
    {
        _logger.LogWarning("🛡️ User requested Safety Kill-Switch RESET.");
        var state = _persistenceService.GetState();
        state.IsSafetyKillSwitchTriggered = false;
        state.GlobalKillSwitchReason = string.Empty;
        _persistenceService.SaveState(state);

        await _hubContext.Clients.All.SendAsync("ReceiveSafetyUpdate", new {
            isTriggered = false,
            reason = string.Empty,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task SetSafetyLimits(decimal maxDrawdownUsd, int maxConsecutiveLosses)
    {
        _logger.LogInformation("🛡️ Updating Safety Limits: Drawdown=${Drawdown}, MaxConsecutiveLosses={Losses}", 
            maxDrawdownUsd, maxConsecutiveLosses);
        
        var state = _persistenceService.GetState();
        state.MaxDrawdownUsd = maxDrawdownUsd;
        state.MaxConsecutiveLosses = maxConsecutiveLosses;
        _persistenceService.SaveState(state);
    }

    public async Task SetRebalanceThreshold(decimal threshold)
    {
        _logger.LogInformation("⚖️ Updating Rebalance Skew Threshold: {Threshold}", threshold);
        var state = _persistenceService.GetState();
        state.MinRebalanceSkewThreshold = threshold;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveRebalanceThresholdUpdate", threshold);
    }

    public async Task SetWalletOverride(string asset, string exchange, string address)
    {
        _logger.LogInformation("🛡️ Updating Wallet Override: {Asset} on {Exchange} -> {Address}", asset, exchange, address);
        var state = _persistenceService.GetState();
        
        if (!state.WalletOverrides.ContainsKey(asset))
        {
            state.WalletOverrides[asset] = new Dictionary<string, string>();
        }
        
        state.WalletOverrides[asset][exchange] = address;
        _persistenceService.SaveState(state);
        
        await _hubContext.Clients.All.SendAsync("ReceiveWalletOverridesUpdate", state.WalletOverrides);
    }

    public async Task SetWalletOverrides(Dictionary<string, Dictionary<string, string>> overrides)
    {
        _logger.LogInformation("🛡️ Updating bulk Wallet Overrides: {Count} assets", overrides.Count);
        var state = _persistenceService.GetState();
        state.WalletOverrides = overrides;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveWalletOverridesUpdate", overrides);
    }

    private async Task PeriodicallyBroadcastStatusAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveStrategyUpdate", new { 
                    threshold = _currentMinProfitThreshold, 
                    reason = _currentStrategyReason 
                }, stoppingToken);
                
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in status broadcast loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

}
