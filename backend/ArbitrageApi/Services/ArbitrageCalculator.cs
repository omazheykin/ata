using ArbitrageApi.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageApi.Services;

public class ArbitrageCalculator
{
    private readonly ILogger<ArbitrageCalculator> _logger;

    public ArbitrageCalculator(ILogger<ArbitrageCalculator> logger)
    {
        _logger = logger;
    }

    public ArbitrageOpportunity? CalculateOpportunity(
        string symbol, 
        Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)> orderBooks, 
        Dictionary<string, (decimal Maker, decimal Taker)> feesDict,
        bool isSandboxMode,
        decimal? minProfitThreshold = null,
        Dictionary<string, List<Balance>>? balances = null,
        decimal safeBalanceMultiplier = 0.3m,
        bool useTakerFees = true,
        Dictionary<string, decimal>? pairThresholds = null)
    {
        // 1. Find best buy and best sell prices across exchanges
        var bestPrices = FindBestPrices(symbol, orderBooks, feesDict);
        if (bestPrices.BestBuy == null || bestPrices.BestSell == null) return null;
        if (bestPrices.BestBuy.Value.Exchange == bestPrices.BestSell.Value.Exchange) return null;

        return CalculatePairOpportunity(
            symbol,
            bestPrices.BestBuy.Value.Exchange,
            bestPrices.BestSell.Value.Exchange,
            bestPrices.BestBuy.Value.Asks,
            bestPrices.BestSell.Value.Bids,
            bestPrices.BestBuy.Value.Fees,
            bestPrices.BestSell.Value.Fees,
            isSandboxMode,
            minProfitThreshold,
            balances,
            safeBalanceMultiplier,
            useTakerFees,
            pairThresholds);
    }

    public ArbitrageOpportunity? CalculatePairOpportunity(
        string symbol,
        string buyExchange,
        string sellExchange,
        List<(decimal Price, decimal Quantity)> asks,
        List<(decimal Price, decimal Quantity)> bids,
        (decimal Maker, decimal Taker) buyFees,
        (decimal Maker, decimal Taker) sellFees,
        bool isSandboxMode,
        decimal? minProfitThreshold = null,
        Dictionary<string, List<Balance>>? balances = null,
        decimal safeBalanceMultiplier = 0.3m,
        bool useTakerFees = true,
        Dictionary<string, decimal>? pairThresholds = null)
    {
        if (asks.Count == 0 || bids.Count == 0) return null;

        // 2. Calculate maximum volume based on liquidity
        var liquidityLimit = Math.Min(
            asks.Sum(a => a.Quantity),
            bids.Sum(b => b.Quantity)
        );

        decimal maxVolume = liquidityLimit;

        if (balances != null)
        {
            var pair = TradingPair.CommonPairs.FirstOrDefault(p => p.Symbol == symbol);
            if (pair != null)
            {
                if (balances.TryGetValue(buyExchange, out var buyBalances) &&
                    balances.TryGetValue(sellExchange, out var sellBalances))
                {
                    var usdBalance = buyBalances.FirstOrDefault(b => b.Asset == pair.QuoteAsset || b.Asset == "USD")?.Free ?? 0m;
                    var assetBalance = sellBalances.FirstOrDefault(b => b.Asset == pair.BaseAsset)?.Free ?? 0m;

                    // Use SafeBalanceMultiplier for risk management
                    var maxVolFromUsd = (usdBalance * safeBalanceMultiplier) / asks[0].Price;
                    var maxVolFromAsset = assetBalance * safeBalanceMultiplier;

                    var balanceLimit = Math.Min(maxVolFromUsd, maxVolFromAsset);
                    maxVolume = Math.Min(liquidityLimit, balanceLimit);
                    maxVolume = Math.Round(maxVolume, 8);
                }
            }
        }

        if (maxVolume <= 0) return null;

        // 3. Simulate order book execution
        var execution = SimulateOrderBookExecution(maxVolume, asks, bids);
        if (execution.BuyVolumeFilled == 0 || execution.SellVolumeFilled == 0) return null;

        // 4. Calculate profit - CONSERVATIVE MODE: Use Taker fees by default if specified
        var buyFee = useTakerFees ? buyFees.Taker : buyFees.Maker;
        var sellFee = useTakerFees ? sellFees.Taker : sellFees.Maker;

        var grossProfitPercentage = ((execution.AvgSellPrice - execution.AvgBuyPrice) / execution.AvgBuyPrice) * 100;
        var netProfitPercentage = grossProfitPercentage - (buyFee * 100) - (sellFee * 100);

        // For stats and volatility tracking, we return anything better than -1%
        if (netProfitPercentage < -1.0m) return null;

        if (netProfitPercentage >= 0.01m) // Only log to console/log file if at least slightly profitable
        {
            _logger.LogInformation("🔍 [LOG-OP] {Symbol} ({BuyExchange}→{SellExchange}): Net {NetProfit:F4}% (Using {FeeType} Fees: {BuyFee}/{SellFee})",
                useTakerFees ? "PESSIMISTIC" : "OPTIMISTIC",
                symbol, buyExchange, sellExchange, netProfitPercentage,
                useTakerFees ? "Taker" : "Maker", buyFee, sellFee);
        }

        if (execution.BuyVolumeFilled >= 0.00001m)
        {
            var pair = TradingPair.CommonPairs.FirstOrDefault(p => p.Symbol == symbol);
            return new ArbitrageOpportunity
            {
                Id = Guid.NewGuid(),
                Asset = pair?.BaseAsset ?? "Unknown",
                Symbol = symbol,
                BuyExchange = buyExchange,
                SellExchange = sellExchange,
                BuyPrice = execution.AvgBuyPrice,
                SellPrice = execution.AvgSellPrice,
                BuyFee = buyFee,
                SellFee = sellFee,
                ProfitPercentage = Math.Round(netProfitPercentage, 4),
                GrossProfitPercentage = Math.Round(grossProfitPercentage, 4),
                Volume = execution.BuyVolumeFilled,
                Timestamp = DateTime.UtcNow,
                Status = "Active",
                IsSandbox = isSandboxMode,
                BuyDepth = asks.Sum(a => a.Quantity),
                SellDepth = bids.Sum(b => b.Quantity)
            };
        }

        return null;
    }

    private ( (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks)? BestBuy, 
              (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids)? BestSell ) 
            FindBestPrices(string symbol, Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)> orderBooks, Dictionary<string, (decimal Maker, decimal Taker)> feesDict)
    {
        (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Asks)? bestBuy = null;
        (string Exchange, decimal Price, (decimal Maker, decimal Taker) Fees, List<(decimal Price, decimal Quantity)> Bids)? bestSell = null;

        foreach (var (exchange, book) in orderBooks)
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

        return (bestBuy, bestSell);
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
}
