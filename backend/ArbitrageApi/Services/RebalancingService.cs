using System.Collections.Concurrent;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;

namespace ArbitrageApi.Services;

public class RebalancingService : BackgroundService
{
    private readonly ILogger<RebalancingService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ITrendAnalysisService _trendService;
    private readonly ChannelProvider _channelProvider;
    private readonly ConcurrentDictionary<string, decimal> _assetSkews = new(); // -1.0 (heavy Coinbase) to 1.0 (heavy Binance)
    private readonly ConcurrentDictionary<string, Dictionary<string, decimal>> _exchangeBalances = new();
    private List<RebalancingProposal> _currentProposals = new();
    private const decimal MinRebalanceUsdValue = 10.0m; // Don't rebalance dust

    public RebalancingService(
        ILogger<RebalancingService> logger, 
        IEnumerable<IExchangeClient> exchangeClients,
        ITrendAnalysisService trendService,
        ChannelProvider channelProvider)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _trendService = trendService;
        _channelProvider = channelProvider;
    }

    public ITrendAnalysisService GetTrendAnalysisService() => _trendService;

    public virtual decimal GetSkew(string asset)
    {
        return _assetSkews.TryGetValue(asset, out var skew) ? skew : 0m;
    }

    public Dictionary<string, decimal> GetAllSkews()
    {
        return new Dictionary<string, decimal>(_assetSkews);
    }
    
    public List<RebalancingProposal> GetProposals()
    {
        return _currentProposals.ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚖️ Rebalancing Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateBalancesAndSkewsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Poll every minute
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating rebalancing skews");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task UpdateBalancesAndSkewsAsync(CancellationToken ct)
    {
        var binance = _exchangeClients.FirstOrDefault(c => c.ExchangeName == "Binance");
        var coinbase = _exchangeClients.FirstOrDefault(c => c.ExchangeName == "Coinbase");

        if (binance == null || coinbase == null) return;

        var binanceBalancesTask = binance.GetBalancesAsync();
        var coinbaseBalancesTask = coinbase.GetBalancesAsync();

        await Task.WhenAll(binanceBalancesTask, coinbaseBalancesTask);

        var binanceBalances = (await binanceBalancesTask).ToDictionary(b => b.Asset, b => b.Free);
        var coinbaseBalances = (await coinbaseBalancesTask).ToDictionary(b => b.Asset, b => b.Free);

        _exchangeBalances["Binance"] = binanceBalances;
        _exchangeBalances["Coinbase"] = coinbaseBalances;

        // Calculate skew for each asset we track
        var assets = binanceBalances.Keys.Union(coinbaseBalances.Keys).Distinct();
        var newProposals = new List<RebalancingProposal>();

        foreach (var asset in assets)
        {
            decimal binanceVal = binanceBalances.TryGetValue(asset, out var b) ? b : 0m;
            decimal coinbaseVal = coinbaseBalances.TryGetValue(asset, out var c) ? c : 0m;
            decimal totalVal = binanceVal + coinbaseVal;

            if (totalVal > 0)
            {
                // Skew = (Binance - Coinbase) / Total
                var skew = (binanceVal - coinbaseVal) / totalVal;
                _assetSkews[asset] = Math.Round(skew, 4);
                
                // Proposal Generation
                if (Math.Abs(skew) > 0.1m) // Only if imbalance is > 10%
                {
                    var proposal = await CalculateProposalAsync(asset, skew, binanceVal, coinbaseVal, binance, coinbase);
                    if (proposal != null && proposal.Amount > 0)
                    {
                        newProposals.Add(proposal);

                        // AUTOMATION TRIGGER (Phase 3)
                        if (proposal.IsViable)
                        {
                            _channelProvider.RebalanceChannel.Writer.TryWrite(proposal);
                        }
                    }
                }
            }
            else
            {
                _assetSkews[asset] = 0m;
            }
        }
        
        _currentProposals = newProposals;

        _logger.LogDebug("⚖️ Updated inventory skews for {Count} assets. Generated {PropCount} proposals.", _assetSkews.Count, _currentProposals.Count);
    }

    private async Task<RebalancingProposal?> CalculateProposalAsync(
        string asset, 
        decimal skew, 
        decimal binanceVal, 
        decimal coinbaseVal,
        IExchangeClient binance,
        IExchangeClient coinbase)
    {
        // Direction
        // Skew > 0 => Binance has more. Move Binance -> Coinbase.
        var fromBinance = skew > 0;
        var source = fromBinance ? binance : coinbase;
        var targetLabel = fromBinance ? "Coinbase" : "Binance";
        
        // Amount to move to reach 0 skew (perfect balance)
        // Target = Total / 2
        // Amount = Current - Target
        var total = binanceVal + coinbaseVal;
        var targetBalance = total / 2;
        var sourceBalance = fromBinance ? binanceVal : coinbaseVal;
        var amountToMove = sourceBalance - targetBalance;
        
        if (amountToMove <= 0) return null;

        // Fee Check
        decimal? fee = await source.GetWithdrawalFeeAsync(asset);
        
        // Trend Check (Phase 3)
        var trend = await _trendService.GetTrendAsync(asset);

        var proposal = new RebalancingProposal
        {
            Asset = asset,
            Skew = skew,
            Direction = fromBinance ? "Binance → Coinbase" : "Coinbase → Binance",
            Amount = Math.Round(amountToMove, 6),
            EstimatedFee = fee ?? 0,
            IsViable = false,
            TrendDescription = trend.Prediction // e.g. "Binance-ward Trend (24h)"
        };

        if (fee.HasValue && amountToMove > 0)
        {
            proposal.CostPercentage = (fee.Value / amountToMove) * 100m;
            
            // Viability Rule: Cost < 1% OR Strong Trend in that direction
            // For now, keep it strictly cost-based, but add the trend description
            proposal.IsViable = proposal.CostPercentage < 1.0m;
        }
        else
        {
            // If we don't know fee, assume caution
            proposal.CostPercentage = 0;
            proposal.IsViable = false; 
        }

        return proposal;
    }
}
