using ArbitrageApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace ArbitrageApi.Services;

public class PassiveRebalancingService : BackgroundService
{
    private readonly ILogger<PassiveRebalancingService> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly IRebalancingService _rebalancingService;
    private readonly OrderExecutionService _executionService;

    // Minimum profit we are willing to accept even for a perfect rebalance (0.01%)
    private const decimal AbsoluteMinProfit = 0.01m;

    public PassiveRebalancingService(
        ILogger<PassiveRebalancingService> logger,
        ChannelProvider channelProvider,
        StatePersistenceService persistenceService,
        IRebalancingService rebalancingService,
        OrderExecutionService executionService)
    {
        _logger = logger;
        _channelProvider = channelProvider;
        _persistenceService = persistenceService;
        _rebalancingService = rebalancingService;
        _executionService = executionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚖️ Passive Rebalancing Service started");
        
        try 
        {
            await foreach (var opportunity in _channelProvider.PassiveRebalanceChannel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessOpportunityAsync(opportunity, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Passive Rebalancing Service stopped.");
        }
    }

    private async Task ProcessOpportunityAsync(ArbitrageOpportunity opportunity, CancellationToken ct)
    {
        var state = _persistenceService.GetState();
        if (state.IsSafetyKillSwitchTriggered) return;
        if (!state.IsAutoTradeEnabled) return;

        // Verify it meets absolute minimum (sanity check)
        if (opportunity.ProfitPercentage < AbsoluteMinProfit) return;

        var asset = opportunity.Asset;
        var skew = _rebalancingService.GetSkew(asset); // -1.0 (heavy CB) to 1.0 (heavy Binance)

        // Skew > 0 means we are heavy on Binance. We want to SELL on Binance (or BUY on Coinbase).
        // Skew < 0 means we are heavy on Coinbase. We want to SELL on Coinbase (or BUY on Binance).

        // Opportunity: BuyExchange -> SellExchange
        bool buyingOnBinance = opportunity.BuyExchange == "Binance";
        bool sellingOnBinance = opportunity.SellExchange == "Binance";
        
        bool buyingOnCoinbase = opportunity.BuyExchange == "Coinbase";
        bool sellingOnCoinbase = opportunity.SellExchange == "Coinbase";
        
        bool improvesSkew = false;
        decimal incentiveScore = 0m;

        if (skew > 0.1m) // Heavily skewed to Binance
        {
            // We want to move funds OUT of Binance (Sell on Binance) OR INTO Coinbase (Buy on Coinbase, implicitly selling elsewhere)
            // Ideal trade: Buy Coinbase -> Sell Binance
            if (sellingOnBinance && buyingOnCoinbase)
            {
                improvesSkew = true;
                incentiveScore = skew; // Higher skew = higher incentive
            }
        }
        else if (skew < -0.1m) // Heavily skewed to Coinbase
        {
            // We want to move funds OUT of Coinbase (Sell on Coinbase) OR INTO Binance
            // Ideal trade: Buy Binance -> Sell Coinbase
            if (sellingOnCoinbase && buyingOnBinance)
            {
                improvesSkew = true;
                incentiveScore = Math.Abs(skew);
            }
        }

        if (improvesSkew)
        {
            // Calculate a "Virtual Threshold"
            // Normally user sets e.g. 0.5%.
            // If incentive is max (1.0 skew), we might accept down to 0.05%.
            // Formula: RequiredThreshold = Max(0.05, UserThreshold - (Skew * 0.4))
            
            var userThreshold = state.PairThresholds.TryGetValue(opportunity.Symbol, out var pairTh) 
                ? pairTh 
                : state.MinProfitThreshold;

            var discount = incentiveScore * 0.4m; // Max 0.4% discount
            var specificThreshold = Math.Max(0.05m, userThreshold - discount); // Never go below 0.05% profit even for great rebalance

            if (opportunity.ProfitPercentage >= specificThreshold)
            {
                _logger.LogInformation("⚖️ PASSIVE REBALANCE: Executing {Symbol} @ {Profit}% (Skew: {Skew:F2}). Trade improves inventory!", 
                    opportunity.Symbol, opportunity.ProfitPercentage, skew);
                
                await _executionService.ExecuteTradeAsync(opportunity, specificThreshold, ct);
            }
            else
            {
                _logger.LogDebug("⚖️ PASSIVE REBALANCE: Skipped {Symbol}. Profit {Profit}% < Discounted Threshold {Th}%", 
                    opportunity.Symbol, opportunity.ProfitPercentage, specificThreshold);
            }
        }
        else
        {
            // Does not improve skew, or skew is neutral.
            // Since this channel only receives "Low Profit" trades (below standard threshold),
            // and this trade doesn't help us rebalance, we just drop it.
        }
    }
}
