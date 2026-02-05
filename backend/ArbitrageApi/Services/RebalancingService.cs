using System.Collections.Concurrent;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;

using Microsoft.AspNetCore.SignalR;
using ArbitrageApi.Hubs;

namespace ArbitrageApi.Services;

public class RebalancingService : BackgroundService, IRebalancingService
{
    private readonly ILogger<RebalancingService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ITrendAnalysisService _trendService;
    private readonly ChannelProvider _channelProvider;
    private readonly StatePersistenceService _persistenceService;
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ConcurrentDictionary<string, decimal> _assetSkews = new(); // -1.0 (heavy Coinbase) to 1.0 (heavy Binance)
    private readonly ConcurrentDictionary<string, Dictionary<string, decimal>> _exchangeBalances = new();
    private List<RebalancingProposal> _currentProposals = new();
    private const decimal MinRebalanceUsdValue = 10.0m; // Don't rebalance dust

    public RebalancingService(
        ILogger<RebalancingService> logger, 
        IEnumerable<IExchangeClient> exchangeClients,
        ITrendAnalysisService trendService,
        ChannelProvider channelProvider,
        StatePersistenceService persistenceService,
        IHubContext<ArbitrageHub> hubContext)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _trendService = trendService;
        _channelProvider = channelProvider;
        _persistenceService = persistenceService;
        _hubContext = hubContext;
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
                
                var state = _persistenceService.GetState();
                
                // Proposal Generation
                if (Math.Abs(skew) > state.MinRebalanceSkewThreshold) // Use configurable threshold (default 10%)
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

            // Wallet Address Resolution (Phase 7)
            // 1. Check for manual override in settings
            var state = _persistenceService.GetState();
            string? depositAddress = null;
            
            if (state.WalletOverrides.TryGetValue(proposal.Asset, out var assetOverrides) &&
                assetOverrides.TryGetValue(targetName, out var manualAddress))
            {
                _logger.LogInformation("🛡️ [AUTO-REBALANCE] Using manual wallet override for {Asset} on {Target}: {Address}", 
                    proposal.Asset, targetName, manualAddress);
                depositAddress = manualAddress;
            }
            
            // 2. Fallback to automated fetching from the TARGET exchange
            if (string.IsNullOrWhiteSpace(depositAddress))
            {
                _logger.LogInformation("🔄 [AUTO-REBALANCE] No manual override found. Fetching deposit address from {Target} API...", targetName);
                depositAddress = await targetClient.GetDepositAddressAsync(proposal.Asset, ct);
            }

            if (string.IsNullOrWhiteSpace(depositAddress))
            {
                _logger.LogError("❌ [AUTO-REBALANCE] Could not resolve deposit address for {Asset} on {Target}. Manual override required.", 
                    proposal.Asset, targetName);
                return false;
            }

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
}
