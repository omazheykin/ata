using ArbitrageApi.Models;
using ArbitrageApi.Hubs;
using ArbitrageApi.Services.Exchanges;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Services;

public class ArbitrageDetectionService : BackgroundService
{
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ILogger<ArbitrageDetectionService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly TradeService _tradeService;
    private readonly List<ArbitrageOpportunity> _recentOpportunities = new();
    private readonly object _lock = new();
    private bool _isSandboxMode = false;
    private int _nextCheckInterval = 2000;

    public bool IsSandboxMode => _isSandboxMode;

    public ArbitrageDetectionService(
        IHubContext<ArbitrageHub> hubContext,
        ILogger<ArbitrageDetectionService> logger,
        IEnumerable<IExchangeClient> exchangeClients,
        TradeService tradeService)
    {
        _hubContext = hubContext;
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _tradeService = tradeService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Arbitrage Detection Service started in {Mode} mode", _isSandboxMode ? "SANDBOX" : "REAL");
        _logger.LogInformation("📡 Monitoring exchanges: {Exchanges}", 
            string.Join(", ", _exchangeClients.Select(c => c.ExchangeName)));

        // Ensure all exchange clients are initialized (especially Coinbase)
        foreach (var client in _exchangeClients)
        {
            if (client is CoinbaseClient coinbase)
            {
                await coinbase.InitializeAsync();
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(_exchangeClients?.Count < 2){
                    _logger.LogWarning("Not enough exchanges to detect arbitrage opportunities");
                    await Task.Delay(10000, stoppingToken);
                    continue;
                }
                var opportunities = await FindArbitrageOpportunities(stoppingToken);

                foreach (var opportunity in opportunities)
                {
                    lock (_lock)
                    {
                        _recentOpportunities.Add(opportunity);

                        // Keep only last 100 opportunities
                        if (_recentOpportunities.Count > 100)
                        {
                            _recentOpportunities.RemoveAt(0);
                        }
                    }

                    // Broadcast to all connected clients
                    await _hubContext.Clients.All.SendAsync("ReceiveOpportunity", opportunity, stoppingToken);

                    // Auto-Trade Logic
                    if (_tradeService.IsAutoTradeEnabled && opportunity.ProfitPercentage >= _tradeService.MinProfitThreshold)
                    {
                        _logger.LogInformation("🤖 Auto-Trade: Profitable opportunity found ({Profit}%), executing...", opportunity.ProfitPercentage);
                        var success = await _tradeService.ExecuteTradeAsync(opportunity);
                        if (success)
                        {
                            // Notify clients about the new transaction
                            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", _tradeService.GetRecentTransactions().First(), stoppingToken);
                        }
                    }

                    _logger.LogInformation(
                        "💰 {Mode} Arbitrage: {Asset} - Buy on {BuyExchange} at ${BuyPrice:N2}, Sell on {SellExchange} at ${SellPrice:N2}, Profit: {Profit:N2}%",
                        _isSandboxMode ? "SANDBOX" : "REAL",
                        opportunity.Asset,
                        opportunity.BuyExchange,
                        opportunity.BuyPrice,
                        opportunity.SellExchange,
                        opportunity.SellPrice,
                        opportunity.ProfitPercentage);
                }

                // Wait 10 seconds before next check (to respect API rate limits)
                await Task.Delay(_nextCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in arbitrage detection service");
                try 
                {
                    await Task.Delay(10000, stoppingToken);
                }
                catch (OperationCanceledException) 
                { 
                    break; 
                }
            }
        }

        _logger.LogInformation("Arbitrage Detection Service stopped");
    }

    private async Task<List<ArbitrageOpportunity>> FindArbitrageOpportunities(CancellationToken cancellationToken)
    {
        var opportunities = new List<ArbitrageOpportunity>();
        try
        {
            var symbols = TradingPair.CommonPairs.Select(p => p.Symbol).ToList();
            // Fetch order books and fees for all exchanges
            var orderBooks = new Dictionary<string, Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>>();
            var feesDict = new Dictionary<string, (decimal Maker, decimal Taker)>();
            foreach (var client in _exchangeClients)
            {
                var fees = await client.GetSpotFeesAsync() ?? (0m, 0m);
                feesDict[client.ExchangeName] = fees;
                var books = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>();
                foreach (var symbol in symbols)
                {
                    var book = await client.GetOrderBookAsync(symbol, 20);
                    if (book != null)
                        books[symbol] = book.Value;
                }
                orderBooks[client.ExchangeName] = books;
            }

            foreach (var symbol in symbols)
            {
                // Find best buy (lowest ask) and best sell (highest bid) across exchanges
                (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks)? bestBuy = null;
                (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids)? bestSell = null;
                foreach (var (exchange, books) in orderBooks)
                {
                    if (books.TryGetValue(symbol, out var book))
                    {
                        if (book.Asks.Count > 0)
                        {
                            var ask = book.Asks[0];
                            if (bestBuy == null || ask.Price < bestBuy.Value.Price)
                                bestBuy = (exchange, ask.Price, feesDict[exchange], book.Asks);
                        }
                        if (book.Bids.Count > 0)
                        {
                            var bid = book.Bids[0];
                            if (bestSell == null || bid.Price > bestSell.Value.Price)
                                bestSell = (exchange, bid.Price, feesDict[exchange], book.Bids);
                        }
                    }
                }
                if (bestBuy == null || bestSell == null) continue;
                if (bestBuy.Value.Exchange == bestSell.Value.Exchange) continue;

                // Simulate order execution for a reasonable volume (e.g., up to 50% of available funds, but limited by order book depth)
                var pair = TradingPair.CommonPairs.First(p => p.Symbol == symbol);
                decimal maxVolume = 1.0m; // Default max volume
                if (_isSandboxMode)
                {
                    var buyExchangeClient = _exchangeClients.First(c => c.ExchangeName == bestBuy.Value.Exchange);
                    var sellExchangeClient = _exchangeClients.First(c => c.ExchangeName == bestSell.Value.Exchange);
                    
                    var buyBalances = await buyExchangeClient.GetBalancesAsync();
                    var sellBalances = await sellExchangeClient.GetBalancesAsync();
                    
                    var usdBalance = buyBalances.FirstOrDefault(b => b.Asset == "USD")?.Free ?? 0m;
                    var assetBalance = sellBalances.FirstOrDefault(b => b.Asset == pair.BaseAsset)?.Free ?? 0m;

                    if (usdBalance > 0 && assetBalance > 0)
                    {
                        var maxVolFromUsd = (usdBalance * 0.5m) / bestBuy.Value.Price;
                        var maxVolFromAsset = assetBalance * 0.5m;
                        maxVolume = Math.Min(maxVolFromUsd, maxVolFromAsset);
                        maxVolume = Math.Round(maxVolume, 8);
                    }
                }
                // Simulate walking the order book for buy (asks)
                decimal buyCost = 0m;
                decimal buyVolumeFilled = 0m;
                foreach (var (price, qty) in bestBuy.Value.Asks)
                {
                    var take = Math.Min(qty, maxVolume - buyVolumeFilled);
                    buyCost += take * price;
                    buyVolumeFilled += take;
                    if (buyVolumeFilled >= maxVolume) break;
                }
                if (buyVolumeFilled == 0) continue;
                decimal avgBuyPrice = buyCost / buyVolumeFilled;

                // Simulate walking the order book for sell (bids)
                decimal sellProceeds = 0m;
                decimal sellVolumeFilled = 0m;
                foreach (var (price, qty) in bestSell.Value.Bids)
                {
                    var take = Math.Min(qty, buyVolumeFilled - sellVolumeFilled);
                    sellProceeds += take * price;
                    sellVolumeFilled += take;
                    if (sellVolumeFilled >= buyVolumeFilled) break;
                }
                if (sellVolumeFilled == 0) continue;
                decimal avgSellPrice = sellProceeds / sellVolumeFilled;

                // Calculate profit percentage after fees
                var buyFee = bestBuy.Value.Fees.Maker;
                var sellFee = bestSell.Value.Fees.Maker;
                var grossProfitPercentage = ((avgSellPrice - avgBuyPrice) / avgBuyPrice) * 100;
                var netProfitPercentage = grossProfitPercentage - buyFee - sellFee;

                if (netProfitPercentage > 0.1m && buyVolumeFilled >= 0.00001m )
                {
                    opportunities.Add(new ArbitrageOpportunity
                    {
                        Id = Guid.NewGuid(),
                        Asset = pair.BaseAsset,
                        BuyExchange = bestBuy.Value.Exchange,
                        SellExchange = bestSell.Value.Exchange,
                        BuyPrice = avgBuyPrice,
                        SellPrice = avgSellPrice,
                        BuyFee = buyFee,
                        SellFee = sellFee,
                        ProfitPercentage = Math.Round(netProfitPercentage, 2),
                        Volume = buyVolumeFilled,
                        Timestamp = DateTime.UtcNow,
                        Status = "Active",
                        IsSandbox = _isSandboxMode
                    });
                }
            }
            _logger.LogInformation("Found {Count} arbitrage opportunities (order book based)", opportunities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding arbitrage opportunities (order book based)");
        }
        return opportunities;
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
        foreach (var client in _exchangeClients)
        {
            try 
            {
                _logger.LogDebug("Updating Sandbox Mode for client: {ExchangeName}", client.ExchangeName);
                client.SetSandboxMode(enabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating sandbox mode for client {ExchangeName}", client.ExchangeName);
            }
        }
        
        // Broadcast the update to all connected clients
        _logger.LogInformation("📡 Broadcasting SandboxModeUpdate: {Status}", enabled);
        await _hubContext.Clients.All.SendAsync("ReceiveSandboxModeUpdate", enabled);
    }
}
