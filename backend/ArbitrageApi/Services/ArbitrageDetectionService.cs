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

    private readonly StatePersistenceService _persistenceService;
    private readonly ArbitrageStatsService _statsService;

    public ArbitrageDetectionService(
        IHubContext<ArbitrageHub> hubContext,
        ILogger<ArbitrageDetectionService> logger,
        IEnumerable<IExchangeClient> exchangeClients,
        TradeService tradeService,
        StatePersistenceService persistenceService,
        ArbitrageStatsService statsService)
    {
        _hubContext = hubContext;
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _tradeService = tradeService;
        _persistenceService = persistenceService;
        _statsService = statsService;

        // Load state from persistence
        var state = _persistenceService.GetState();
        _isSandboxMode = state.IsSandboxMode;
        
        // Apply sandbox mode to all clients immediately
        foreach (var client in _exchangeClients)
        {
            client.SetSandboxMode(_isSandboxMode);
        }
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
                        // CALENDAR INTEGRATION: Check activity zone before trading
                        var stats = await _statsService.GetStatsAsync();
                        var now = DateTime.UtcNow;
                        var day = now.DayOfWeek.ToString().Substring(0, 3);
                        var hour = now.Hour.ToString("D2");

                        if (stats.Calendar.TryGetValue(day, out var dayHours) && dayHours.TryGetValue(hour, out var detail))
                        {
                            if (detail.Zone == "low_activity")
                            {
                                _logger.LogInformation("📉 Auto-Trade: Skipping profitable opportunity ({Profit}%) because current activity zone is LOW.", opportunity.ProfitPercentage);
                                continue;
                            }
                        }

                        _logger.LogInformation("🤖 Auto-Trade: Profitable opportunity found ({Profit}%), queuing for execution...", opportunity.ProfitPercentage);
                        await _tradeService.QueueTradeAsync(opportunity);
                        
                        // Note: Transaction notification will now happen in TradeService or via a separate event
                        // For now, we'll let the UI update via the regular transaction polling or SignalR from TradeService if implemented
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
            
            // 1. Fetch market data (order books and fees)
            var (orderBooks, feesDict) = await FetchMarketDataAsync(symbols, cancellationToken);

            foreach (var symbol in symbols)
            {
                // 2. Find best buy and best sell prices across exchanges
                var bestPrices = FindBestPrices(symbol, orderBooks, feesDict);
                if (bestPrices.BestBuy == null || bestPrices.BestSell == null) continue;
                if (bestPrices.BestBuy.Value.Exchange == bestPrices.BestSell.Value.Exchange) continue;

                var bestBuy = bestPrices.BestBuy.Value;
                var bestSell = bestPrices.BestSell.Value;

                // 3. Calculate maximum volume based on available funds (Sandbox) or default
                var pair = TradingPair.CommonPairs.First(p => p.Symbol == symbol);
                decimal maxVolume = await CalculateMaxVolumeAsync(pair, bestBuy, bestSell);
                if (maxVolume <= 0) continue;

                // 4. Simulate order book execution
                var execution = SimulateOrderBookExecution(maxVolume, bestBuy.Asks, bestSell.Bids);
                if (execution.BuyVolumeFilled == 0 || execution.SellVolumeFilled == 0) continue;

                // 5. Calculate profit and create opportunity if profitable
                var opportunity = CreateOpportunity(pair, symbol, bestBuy, bestSell, execution);
                
                // 6. Queue event for statistics (even if not profitable enough to trade)
                await _statsService.QueueEventAsync(new ArbitrageEvent
                {
                    Id = Guid.NewGuid(),
                    Pair = symbol,
                    Direction = $"{bestBuy.Exchange.Substring(0, 1)}→{bestSell.Exchange.Substring(0, 1)}",
                    Spread = (execution.AvgSellPrice - execution.AvgBuyPrice) / execution.AvgBuyPrice,
                    DepthBuy = bestBuy.Asks.Sum(a => a.Quantity),
                    DepthSell = bestSell.Bids.Sum(b => b.Quantity),
                    Timestamp = DateTime.UtcNow
                });

                if (opportunity != null)
                {
                    opportunities.Add(opportunity);
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

    private async Task<(Dictionary<string, Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>> OrderBooks, Dictionary<string, (decimal Maker, decimal Taker)> Fees)> FetchMarketDataAsync(List<string> symbols, CancellationToken ct)
    {
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

        return (orderBooks, feesDict);
    }

    private ( (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks)? BestBuy, 
              (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids)? BestSell ) 
            FindBestPrices(string symbol, Dictionary<string, Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>> orderBooks, Dictionary<string, (decimal Maker, decimal Taker)> feesDict)
    {
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

        return (bestBuy, bestSell);
    }

    private async Task<decimal> CalculateMaxVolumeAsync(TradingPair pair, (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks) bestBuy, (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids) bestSell)
    {
        decimal maxVolume = 1.0m; // Default for real mode

        if (_isSandboxMode)
        {
            var buyExchangeClient = _exchangeClients.First(c => c.ExchangeName == bestBuy.Exchange);
            var sellExchangeClient = _exchangeClients.First(c => c.ExchangeName == bestSell.Exchange);
            
            var buyBalances = await buyExchangeClient.GetBalancesAsync();
            var sellBalances = await sellExchangeClient.GetBalancesAsync();
            
            var usdBalance = buyBalances.FirstOrDefault(b => b.Asset == "USD")?.Free ?? 0m;
            var assetBalance = sellBalances.FirstOrDefault(b => b.Asset == pair.BaseAsset)?.Free ?? 0m;

            var maxVolFromUsd = (usdBalance * 0.5m) / bestBuy.Price;
            var maxVolFromAsset = assetBalance * 0.5m;
            
            maxVolume = Math.Min(maxVolFromUsd, maxVolFromAsset);
            maxVolume = Math.Round(maxVolume, 8);

            if (maxVolume <= 0)
            {
                _logger.LogDebug("⏭️ Skipping {Symbol} opportunity: Insufficient funds in Sandbox (USD: {Usd}, {Asset}: {AssetBal})", 
                    pair.Symbol, usdBalance, pair.BaseAsset, assetBalance);
            }
        }

        return maxVolume;
    }

    private (decimal BuyVolumeFilled, decimal AvgBuyPrice, decimal SellVolumeFilled, decimal AvgSellPrice) SimulateOrderBookExecution(decimal maxVolume, List<(decimal Price, decimal Quantity)> asks, List<(decimal Price, decimal Quantity)> bids)
    {
        // Simulate walking the order book for buy (asks)
        decimal buyCost = 0m;
        decimal buyVolumeFilled = 0m;
        foreach (var (price, qty) in asks)
        {
            var take = Math.Min(qty, maxVolume - buyVolumeFilled);
            buyCost += take * price;
            buyVolumeFilled += take;
            if (buyVolumeFilled >= maxVolume) break;
        }

        if (buyVolumeFilled == 0) return (0, 0, 0, 0);
        decimal avgBuyPrice = buyCost / buyVolumeFilled;

        // Simulate walking the order book for sell (bids)
        decimal sellProceeds = 0m;
        decimal sellVolumeFilled = 0m;
        foreach (var (price, qty) in bids)
        {
            var take = Math.Min(qty, buyVolumeFilled - sellVolumeFilled);
            sellProceeds += take * price;
            sellVolumeFilled += take;
            if (sellVolumeFilled >= buyVolumeFilled) break;
        }

        if (sellVolumeFilled == 0) return (buyVolumeFilled, avgBuyPrice, 0, 0);
        decimal avgSellPrice = sellProceeds / sellVolumeFilled;

        return (buyVolumeFilled, avgBuyPrice, sellVolumeFilled, avgSellPrice);
    }

    private ArbitrageOpportunity? CreateOpportunity(TradingPair pair, string symbol, (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks) bestBuy, (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids) bestSell, (decimal BuyVolumeFilled, decimal AvgBuyPrice, decimal SellVolumeFilled, decimal AvgSellPrice) execution)
    {
        var buyFee = bestBuy.Fees.Maker;
        var sellFee = bestSell.Fees.Maker;
        var grossProfitPercentage = ((execution.AvgSellPrice - execution.AvgBuyPrice) / execution.AvgBuyPrice) * 100;
        var netProfitPercentage = grossProfitPercentage - buyFee - sellFee;

        if (netProfitPercentage > 0.1m && execution.BuyVolumeFilled >= 0.00001m)
        {
            return new ArbitrageOpportunity
            {
                Id = Guid.NewGuid(),
                Asset = pair.BaseAsset,
                Symbol = symbol,
                BuyExchange = bestBuy.Exchange,
                SellExchange = bestSell.Exchange,
                BuyPrice = execution.AvgBuyPrice,
                SellPrice = execution.AvgSellPrice,
                BuyFee = buyFee,
                SellFee = sellFee,
                ProfitPercentage = Math.Round(netProfitPercentage, 2),
                Volume = execution.BuyVolumeFilled,
                Timestamp = DateTime.UtcNow,
                Status = "Active",
                IsSandbox = _isSandboxMode
            };
        }

        return null;
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
