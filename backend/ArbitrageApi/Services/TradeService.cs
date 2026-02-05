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
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ConcurrentQueue<Transaction> _transactions = new();
    private bool _isAutoTradeEnabled = false;
    private decimal _minProfitThreshold = 0.5m; // 0.5% default
    private ExecutionStrategy _strategy = ExecutionStrategy.Sequential;
    private decimal _maxSlippagePercentage = 0.2m; // 0.2% default
    private readonly ChannelProvider _channelProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> _hubContext;
    private readonly ArbitrageStatsService _statsService;
    private readonly RebalancingService _rebalancingService;

    public TradeService(
        ILogger<TradeService> logger, 
        IEnumerable<IExchangeClient> exchangeClients, 
        IConfiguration configuration, 
        StatePersistenceService persistenceService,
        Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> hubContext,
        ChannelProvider channelProvider,
        ArbitrageStatsService statsService,
        RebalancingService rebalancingService)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _persistenceService = persistenceService;
        _hubContext = hubContext;
        _channelProvider = channelProvider;
        _statsService = statsService;
        _rebalancingService = rebalancingService;
        
        // Load state from persistence
        var state = _persistenceService.GetState();
        _isAutoTradeEnabled = state.IsAutoTradeEnabled;
        _minProfitThreshold = state.MinProfitThreshold;
        _logger.LogInformation("Loaded state: AutoTrade={AutoTrade}, MinProfit={MinProfit}%", _isAutoTradeEnabled, _minProfitThreshold);

        var configStrategy = configuration.GetValue<string>("Trading:ExecutionStrategy");
        if (!string.IsNullOrEmpty(configStrategy) && Enum.TryParse<ExecutionStrategy>(configStrategy, true, out var strategy))
        {
            _strategy = strategy;
            _logger.LogInformation("Loaded Execution Strategy from config: {Strategy}", _strategy);
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

                    // REBALANCING LOGIC: Adjust threshold based on skew
                    var asset = opportunity.Asset;
                    var skew = _rebalancingService.GetSkew(asset); // -1.0 (heavy CB) to 1.0 (heavy Binance)
                    
                    // If buying on Coinbase (CB) and selling on Binance (B), we are MOVING ASSET TO BINANCE (increasing skew)
                    bool movingToBinance = opportunity.BuyExchange == "Coinbase";
                    
                    decimal thresholdAdjustment = 0m;
                    if (movingToBinance)
                    {
                        // If we are already heavy on Binance (skew > 0), this trade makes it WORSE.
                        if (skew > 0) thresholdAdjustment = skew * 0.5m; // Max +0.5% penalty
                        // If we are heavy on Coinbase (skew < 0), this trade IMPROVES balance.
                        else if (skew < 0) thresholdAdjustment = skew * 0.3m; // Max -0.3% incentive
                    }
                    else // movingToCoinbase
                    {
                        if (skew < 0) thresholdAdjustment = Math.Abs(skew) * 0.5m;
                        else if (skew > 0) thresholdAdjustment = -skew * 0.3m;
                    }

                    var adjustedThreshold = Math.Max(0.05m, _minProfitThreshold + thresholdAdjustment);
                    
                    if (opportunity.ProfitPercentage < adjustedThreshold)
                    {
                        _logger.LogInformation("⚖️ Rebalancing: Skipping {Symbol} ({Profit}%). Adjusted threshold: {Threshold:N2}% (Base: {Base}%, Skew: {Skew:N2})", 
                            opportunity.Symbol, opportunity.ProfitPercentage, adjustedThreshold, _minProfitThreshold, skew);
                        continue;
                    }

                    await ExecuteTradeAsync(opportunity, stoppingToken);
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
                    await ExecuteRebalanceAsync(proposal, stoppingToken);
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
    public ExecutionStrategy Strategy => _strategy;
    public decimal MaxSlippagePercentage => _maxSlippagePercentage;

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

    public void SetExecutionStrategy(ExecutionStrategy strategy)
    {
        _strategy = strategy;
        _logger.LogInformation("Execution Strategy set to {Strategy}", strategy);
    }

    public async Task<bool> ExecuteRebalanceAsync(RebalancingProposal proposal, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("⚖️ [AUTO-REBALANCE] Initiating transfer for {Asset}: {Amount} {Direction}", 
                proposal.Asset, proposal.Amount, proposal.Direction);

            var sourceName = proposal.Direction.Split(' ')[0];
            var targetName = proposal.Direction.Split(' ')[2];
            
            var sourceClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == sourceName);
            var targetClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == targetName);

            if (sourceClient == null || targetClient == null)
            {
                _logger.LogError("Missing exchange client for rebalance: {Source} or {Target}", sourceName, targetName);
                return false;
            }

            // In a real system, we'd need the deposit address from the TARGET exchange.
            // For now, we'll use a placeholder or config.
            string depositAddress = "MOCK_ADDRESS_REPLACE_WITH_REAL";
            
            var txId = await sourceClient.WithdrawAsync(proposal.Asset, proposal.Amount, depositAddress);
            
            _logger.LogInformation("✅ [AUTO-REBALANCE] Transfer submitted! TxId: {TxId}", txId);
            
            // Record a special transaction for history
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "Rebalance",
                Asset = proposal.Asset,
                Amount = proposal.Amount,
                Exchange = proposal.Direction,
                Status = "Success",
                BuyOrderId = txId // Store TxId here for lack of better field
            };
            
            _channelProvider.TransactionChannel.Writer.TryWrite(transaction);
            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", transaction, ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute auto-rebalance for {Asset}", proposal.Asset);
            return false;
        }
    }

    public async Task<bool> ExecuteTradeAsync(ArbitrageOpportunity opportunity, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("🚀 Executing arbitrage trade for {Asset} using {Strategy} strategy...", opportunity.Asset, _strategy);
            
            var buyClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == opportunity.BuyExchange);
            var sellClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == opportunity.SellExchange);

            if (buyClient == null || sellClient == null)
            {
                _logger.LogError("Missing exchange client for trade execution.");
                return false;
            }

            // 1. Slippage Check
            var currentBuyPrice = await buyClient.GetPriceAsync(opportunity.Symbol);
            var currentSellPrice = await sellClient.GetPriceAsync(opportunity.Symbol);

            if (currentBuyPrice != null && currentSellPrice != null)
            {
                var currentSpread = ((currentSellPrice.Price - currentBuyPrice.Price) / currentBuyPrice.Price) * 100;
                if (currentSpread < _minProfitThreshold)
                {
                    _logger.LogWarning("⚠️ Trade aborted: Slippage exceeded. Current spread {Spread:N2}% < Threshold {Threshold}%", currentSpread, _minProfitThreshold);
                    return false;
                }
                _logger.LogInformation("✅ Slippage check passed: Current spread {Spread:N2}% >= Threshold {Threshold}%", currentSpread, _minProfitThreshold);
            }
            else
            {
                _logger.LogWarning("⚠️ Slippage check SKIPPED: Could not fetch current prices for {Symbol}. Proceeding with caution...", opportunity.Symbol);
            }

            if (_strategy == ExecutionStrategy.Sequential)
            {
                return await ExecuteSequentialAsync(opportunity, buyClient, sellClient, ct);
            }
            else
            {
                return await ExecuteConcurrentAsync(opportunity, buyClient, sellClient, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute trade for {Symbol}", opportunity.Symbol);
            return false;
        }
    }

    private async Task<bool> ExecuteSequentialAsync(ArbitrageOpportunity opportunity, IExchangeClient buyClient, IExchangeClient sellClient, CancellationToken ct)
    {
        // 1. Place Buy Order
        var buyResponse = await buyClient.PlaceMarketBuyOrderAsync(opportunity.Symbol, opportunity.Volume);
        
        if (buyResponse.Status != OrderStatus.Filled && buyResponse.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogError("❌ Sequential Trade Failed: Buy order failed on {Exchange} for {Symbol}. Error: {Error}", opportunity.BuyExchange, opportunity.Symbol, buyResponse.ErrorMessage);
            RecordTransaction(opportunity, buyResponse, null, "Failed");
            return false;
        }

        _logger.LogInformation("✅ Buy order filled on {Exchange} for {Symbol}. Placing sell order on {SellEx}...", opportunity.BuyExchange, opportunity.Symbol, opportunity.SellExchange);

        // 2. Place Sell Order
        var sellResponse = await sellClient.PlaceMarketSellOrderAsync(opportunity.Symbol, buyResponse.ExecutedQuantity);

        if (sellResponse.Status != OrderStatus.Filled && sellResponse.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogCritical("⚠️ CRITICAL: Buy order filled but Sell order FAILED on {Exchange} for {Symbol}. Triggering UNDO logic...", opportunity.SellExchange, opportunity.Symbol);
            
            // 3. Recovery (Undo) Logic
            var undoResponse = await buyClient.PlaceMarketSellOrderAsync(opportunity.Symbol, buyResponse.ExecutedQuantity);
            
            var status = undoResponse.Status == OrderStatus.Filled ? "Recovered" : "One-Sided Fill (CRITICAL)";
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, status);
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            transaction.RecoveryOrderId = undoResponse.OrderId;

            return false;
        }

        _logger.LogInformation("✅ Sequential trade completed successfully for {Symbol}!", opportunity.Symbol);
        RecordTransaction(opportunity, buyResponse, sellResponse, "Success");
        return true;
    }

    private async Task<bool> ExecuteConcurrentAsync(ArbitrageOpportunity opportunity, IExchangeClient buyClient, IExchangeClient sellClient, CancellationToken ct)
    {
        // Place both orders simultaneously
        var buyTask = buyClient.PlaceMarketBuyOrderAsync(opportunity.Symbol, opportunity.Volume);
        var sellTask = sellClient.PlaceMarketSellOrderAsync(opportunity.Symbol, opportunity.Volume);

        await Task.WhenAll(buyTask, sellTask);

        var buyResponse = await buyTask;
        var sellResponse = await sellTask;

        bool buySuccess = buyResponse.Status == OrderStatus.Filled || buyResponse.Status == OrderStatus.PartiallyFilled;
        bool sellSuccess = sellResponse.Status == OrderStatus.Filled || sellResponse.Status == OrderStatus.PartiallyFilled;

        if (buySuccess && sellSuccess)
        {
            _logger.LogInformation("✅ Concurrent trade completed successfully for {Symbol}!", opportunity.Symbol);
            RecordTransaction(opportunity, buyResponse, sellResponse, "Success");
            return true;
        }

        // Handle one-sided failures
        if (buySuccess && !sellSuccess)
        {
            _logger.LogCritical("⚠️ Concurrent Trade One-Sided: Buy succeeded, Sell failed for {Symbol}. Triggering UNDO on Buy exchange...", opportunity.Symbol);
            var undoResponse = await buyClient.PlaceMarketSellOrderAsync(opportunity.Symbol, buyResponse.ExecutedQuantity);
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, "Recovered");
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            return false;
        }
        
        if (!buySuccess && sellSuccess)
        {
            _logger.LogCritical("⚠️ Concurrent Trade One-Sided: Sell succeeded, Buy failed for {Symbol}. Triggering UNDO on Sell exchange...", opportunity.Symbol);
            var undoResponse = await sellClient.PlaceMarketBuyOrderAsync(opportunity.Symbol, sellResponse.ExecutedQuantity);
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, "Recovered");
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            return false;
        }

        _logger.LogError("❌ Concurrent Trade Failed: Both orders failed for {Symbol}.", opportunity.Symbol);
        RecordTransaction(opportunity, buyResponse, sellResponse, "Failed");
        return false;
    }

    private Transaction RecordTransaction(ArbitrageOpportunity opportunity, OrderResponse buy, OrderResponse? sell, string status)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "Arbitrage",
            Asset = opportunity.Asset,
            Amount = buy.ExecutedQuantity,
            Exchange = $"{opportunity.BuyExchange} → {opportunity.SellExchange}",
            Price = buy.AveragePrice ?? buy.Price ?? 0m,
            Fee = ((buy.Price ?? 0m) * buy.ExecutedQuantity * 0.001m) + ((sell?.Price ?? 0m) * (sell?.ExecutedQuantity ?? 0m) * 0.001m),
            Profit = status == "Success" && sell?.Price != null && buy.Price != null 
                ? (sell.Price.Value - buy.Price.Value) * buy.ExecutedQuantity 
                : 0m,
            Status = status,
            BuyOrderId = buy.OrderId,
            SellOrderId = sell?.OrderId,
            BuyOrderStatus = buy.Status.ToString(),
            SellOrderStatus = sell?.Status.ToString(),
            Strategy = _strategy
        };

        _transactions.Enqueue(transaction);
        while (_transactions.Count > 50) _transactions.TryDequeue(out _);
        
        // Broadcast to SignalR clients
        _hubContext.Clients.All.SendAsync("ReceiveTransaction", transaction).ConfigureAwait(false);
        
        // Emit to stats engine for persistence
        _channelProvider.TransactionChannel.Writer.TryWrite(transaction);
        
        return transaction;
    }

    public List<Transaction> GetRecentTransactions()
    {
        return _transactions.Reverse().ToList();
    }
}
