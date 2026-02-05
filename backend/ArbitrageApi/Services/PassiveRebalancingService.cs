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
        
        // N-Exchange Logic: check deviation of Source (Sell) and Target (Buy)
        var sellDeviation = _rebalancingService.GetDeviation(asset, opportunity.SellExchange);
        var buyDeviation = _rebalancingService.GetDeviation(asset, opportunity.BuyExchange);

        bool improvesSkew = false;
        decimal incentiveScore = 0m;
        
        // Use the configured threshold (e.g. 0.1 / 10%)
        decimal threshold = state.MinRebalanceSkewThreshold;

        // Ideal Scenario: Moving from Overweight (> Threshold) to Underweight (< -Threshold)
        // ex: Selling on Exchange A (+0.5) to Buy on Exchange B (-0.5)
        if (sellDeviation > threshold && buyDeviation < -threshold)
        {
            improvesSkew = true;
            // Combined magnitude of the correction
            incentiveScore = sellDeviation + Math.Abs(buyDeviation);
        }
        // Desperate Scenario: Source is Extremely Overweight (> 2x Threshold)
        // We need to sell regardless of target state (unless target is also super heavy, which is unlikely due to mean property)
        else if (sellDeviation > (threshold * 2))
        {
             improvesSkew = true;
             incentiveScore = sellDeviation; 
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
                _logger.LogInformation("⚖️ PASSIVE REBALANCE: Executing {Symbol} @ {Profit}% (Score: {Score:F2}). Trade improves inventory!", 
                    opportunity.Symbol, opportunity.ProfitPercentage, incentiveScore);
                
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
