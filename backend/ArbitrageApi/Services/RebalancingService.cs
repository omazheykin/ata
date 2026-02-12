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

    // Asset -> Exchange -> Deviation (e.g. BTC -> Binance -> +0.5)
    // Deviation is (Balance - Mean) / Mean. 
    // Wait, simpler: Deviation is Absolute Amount Diff? Or Percentage?
    // Let's use Normalized Deviation: (Balance - Mean) / Mean.
    // +0.5 means 50% above mean. -0.5 means 50% below mean.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, decimal>> _assetDeviations = new();
    
    // Asset -> Exchange -> Balance
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, decimal>> _exchangeBalances = new();
    
    private List<RebalancingProposal> _currentProposals = new();
    private const decimal MinRebalanceUsdValue = 10.0m;

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

    public decimal GetDeviation(string asset, string exchange)
    {
        if (_assetDeviations.TryGetValue(asset, out var exchangeDevs) && 
            exchangeDevs.TryGetValue(exchange, out var deviation))
        {
            return deviation;
        }
        return 0m;
    }

    public Dictionary<string, Dictionary<string, decimal>> GetAllDeviations()
    {
        // Deep copy for thread safety
        return _assetDeviations.ToDictionary(
            kvp => kvp.Key,
            kvp => new Dictionary<string, decimal>(kvp.Value));
    }
    
    public List<RebalancingProposal> GetProposals() => _currentProposals.ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚖️ Rebalancing Service started (N-Exchange Support)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("⚖️ [REBALANCE] Updating balances...");
                await UpdateBalancesAndSkewsAsync(stoppingToken);
                _logger.LogDebug("⚖️ [REBALANCE] Cycle complete. Waiting 1m...");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "❌ [REBALANCE] FATAL CRASH in update cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task UpdateBalancesAndSkewsAsync(CancellationToken ct)
    {
        if (!_exchangeClients.Any()) return;

        // 1. Fetch Balances
        var balanceTasks = _exchangeClients.ToDictionary(
            client => client.ExchangeName, 
            client => client.GetBalancesAsync());

        await Task.WhenAll(balanceTasks.Values);

        // 2. Aggregate Data
        // Map: Asset -> Exchange -> Balance
        var balMap = new Dictionary<string, Dictionary<string, decimal>>();
        
        foreach (var (exchange, task) in balanceTasks)
        {
            var balances = await task;
            foreach (var b in balances)
            {
                if (!balMap.ContainsKey(b.Asset)) balMap[b.Asset] = new Dictionary<string, decimal>();
                balMap[b.Asset][exchange] = b.Free;
            }
        }

        // 3. Calculate Stats
        var newProposals = new List<RebalancingProposal>();
        var state = _persistenceService.GetState();

        foreach (var asset in balMap.Keys)
        {
            var exchangeBals = balMap[asset];
            
            // Ensure all exchanges are represented (fill 0s)
            foreach (var client in _exchangeClients)
            {
                if (!exchangeBals.ContainsKey(client.ExchangeName))
                    exchangeBals[client.ExchangeName] = 0m;
            }

            decimal total = exchangeBals.Values.Sum();
            int count = _exchangeClients.Count;
            
            if (total > 0)
            {
                decimal mean = total / count;
                
                // Store Balances
                // Use GetOrAdd to ensure nested dictionary exists
                var assetBalDict = _exchangeBalances.GetOrAdd(asset, _ => new ConcurrentDictionary<string, decimal>());
                foreach (var kvp in exchangeBals) assetBalDict[kvp.Key] = kvp.Value;

                // Calculate Deviations
                // Deviation = (Balance - Mean) / Total  <-- Standardized by Total or Mean?
                // Let's use: Contribution to Imbalance.
                // Simple Deviation: Balance - Mean.
                // Normalized: (Balance - Mean) / Total.
                // Previous code: (Binance - Coinbase) / Total.
                // If B=100, C=0. Total=100. (100-0)/100 = 1.0 (Full Skew).
                // New logic: B=100. Mean=50. Dev = 50. Norm = 50/100 = 0.5.
                // C=0. Mean=50. Dev = -50. Norm = -50/100 = -0.5.
                // So +0.5 is "Fully heavy on this side" relative to 2 exchanges.
                
                var assetDevDict = _assetDeviations.GetOrAdd(asset, _ => new ConcurrentDictionary<string, decimal>());
                
                foreach (var kvp in exchangeBals)
                {
                    decimal deviation = kvp.Value - mean;
                    decimal normalizedSkew = deviation / total; 
                    // Store normalized skew (-1.0 to 1.0 effectively, though sum is 0)
                    assetDevDict[kvp.Key] = Math.Round(normalizedSkew, 4);
                }

                // Proposal Generation
                // Find Heaviest and Lightest
                var heaviest = exchangeBals.MaxBy(kvp => kvp.Value);
                var lightest = exchangeBals.MinBy(kvp => kvp.Value);

                decimal maxDev = heaviest.Value - mean;
                
                // Determine Threshold Trigger
                // User Threshold (e.g., 0.1 / 10%) should compare to what?
                // (Max - Min) / Total > Threshold?
                // Or Max's Contribution?
                // Let's use (Max - Min) / Total to capture the "Spread" of imbalance.
                
                decimal spread = (heaviest.Value - lightest.Value) / total;
                
                if (spread > state.MinRebalanceSkewThreshold)
                {
                    var proposal = await CalculateProposalAsync(asset, heaviest, lightest, total, ct);
                    if (proposal != null && proposal.Amount > 0)
                    {
                        newProposals.Add(proposal);
                        if (proposal.IsViable)
                        {
                            _channelProvider.RebalanceChannel.Writer.TryWrite(proposal);
                        }
                    }
                }
            }
        }
        
        _currentProposals = newProposals;
        _logger.LogDebug("⚖️ Updated inventory metrics for {Count} assets (N-Exchange).", balMap.Count);
    }

    private async Task<RebalancingProposal?> CalculateProposalAsync(
        string asset, 
        KeyValuePair<string, decimal> heavy, 
        KeyValuePair<string, decimal> light, 
        decimal total,
        CancellationToken ct)
    {
        string sourceName = heavy.Key;
        string targetName = light.Key;
        decimal sourceBal = heavy.Value;
        decimal targetBal = light.Value;

        // Algorithm: Move enough to equalize these two specific exchanges
        // (Source + Target) / 2 = Target Equilibrium for these two.
        // Amount = Source - ((Source + Target) / 2) = (Source - Target) / 2.
        decimal amountToMove = (sourceBal - targetBal) / 2;
        
        if (amountToMove <= 0) return null;

        var sourceClient = _exchangeClients.First(c => c.ExchangeName == sourceName);
        decimal? fee = await sourceClient.GetWithdrawalFeeAsync(asset);
        var trend = await _trendService.GetTrendAsync(asset, ct);

        // Calculate "Global Skew" for UI representation
        // Just return the Spread we calculated earlier as "Skew" for compatibility?
        // Or the normalized deviation of the Source.
        decimal skewDisplay = (sourceBal - targetBal) / total; 

        var proposal = new RebalancingProposal
        {
            Asset = asset,
            Skew = Math.Round(skewDisplay, 4), // Represents the severity of the imbalance between these two
            Direction = $"{sourceName} → {targetName}",
            Amount = Math.Round(amountToMove, 6),
            EstimatedFee = fee ?? 0,
            IsViable = false,
            TrendDescription = trend.Prediction
        };

        if (fee.HasValue && amountToMove > 0)
        {
            proposal.CostPercentage = (fee.Value / amountToMove) * 100m;
            proposal.IsViable = proposal.CostPercentage < 1.0m;
        }

        return proposal;
    }

    public virtual async Task<bool> ExecuteRebalanceAsync(RebalancingProposal proposal, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("⚖️ [AUTO-REBALANCE] Initiating transfer for {Asset}: {Amount} {Direction}", 
                proposal.Asset, proposal.Amount, proposal.Direction);

            // Parse "Binance → Coinbase"
            var parts = proposal.Direction.Split(new[] { " → " }, StringSplitOptions.None);
            if (parts.Length != 2) return false;
            
            var sourceName = parts[0];
            var targetName = parts[1];
            
            var sourceClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == sourceName);
            var targetClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName == targetName);

            if (sourceClient == null || targetClient == null)
            {
                _logger.LogError("Missing exchange client: {Source} or {Target}", sourceName, targetName);
                return false;
            }

            // Wallet Address Logic
            var state = _persistenceService.GetState();
            string? depositAddress = null;
            
            if (state.WalletOverrides.TryGetValue(proposal.Asset, out var assetOverrides) &&
                assetOverrides.TryGetValue(targetName, out var manualAddress))
            {
                depositAddress = manualAddress;
            }
            
            if (string.IsNullOrWhiteSpace(depositAddress))
            {
                depositAddress = await targetClient.GetDepositAddressAsync(proposal.Asset, ct);
            }

            if (string.IsNullOrWhiteSpace(depositAddress))
            {
                _logger.LogError("❌ [AUTO-REBALANCE] No deposit address for {Target}", targetName);
                return false;
            }

            var txId = await sourceClient.WithdrawAsync(proposal.Asset, proposal.Amount, depositAddress);
            
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "Rebalance",
                Asset = proposal.Asset,
                Amount = proposal.Amount,
                Exchange = proposal.Direction,
                Status = "Success",
                BuyOrderId = txId
            };
            
            _channelProvider.TransactionChannel.Writer.TryWrite(transaction);
            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", transaction, ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute rebalance");
            return false;
        }
    }
}
