using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ArbitrageApi.Services;

public class TradeService
{
    private readonly ILogger<TradeService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ConcurrentQueue<Transaction> _transactions = new();
    private bool _isAutoTradeEnabled = false;
    private decimal _minProfitThreshold = 0.5m; // 0.5% default
    private ExecutionStrategy _strategy = ExecutionStrategy.Sequential;
    private decimal _maxSlippagePercentage = 0.2m; // 0.2% default

    public TradeService(ILogger<TradeService> logger, IEnumerable<IExchangeClient> exchangeClients, IConfiguration configuration)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        
        // Load threshold from configuration if available
        var configThreshold = configuration.GetValue<decimal?>("Trading:MinProfitThreshold");
        if (configThreshold.HasValue)
        {
            _minProfitThreshold = configThreshold.Value;
            _logger.LogInformation("Loaded Min Profit Threshold from config: {Threshold}%", _minProfitThreshold);
        }

        var configStrategy = configuration.GetValue<string>("Trading:ExecutionStrategy");
        if (!string.IsNullOrEmpty(configStrategy) && Enum.TryParse<ExecutionStrategy>(configStrategy, true, out var strategy))
        {
            _strategy = strategy;
            _logger.LogInformation("Loaded Execution Strategy from config: {Strategy}", _strategy);
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
    }

    public void SetMinProfitThreshold(decimal threshold)
    {
        _minProfitThreshold = threshold;
        _logger.LogInformation("Min Profit Threshold set to {Threshold}%", threshold);
    }

    public void SetExecutionStrategy(ExecutionStrategy strategy)
    {
        _strategy = strategy;
        _logger.LogInformation("Execution Strategy set to {Strategy}", strategy);
    }

    public async Task<bool> ExecuteTradeAsync(ArbitrageOpportunity opportunity)
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
            var currentBuyPrice = await buyClient.GetPriceAsync(opportunity.Asset + "USD"); // Simplified symbol
            var currentSellPrice = await sellClient.GetPriceAsync(opportunity.Asset + "USD");

            if (currentBuyPrice != null && currentSellPrice != null)
            {
                var currentSpread = ((currentSellPrice.Price - currentBuyPrice.Price) / currentBuyPrice.Price) * 100;
                if (currentSpread < _minProfitThreshold)
                {
                    _logger.LogWarning("⚠️ Trade aborted: Slippage exceeded. Current spread {Spread:N2}% < Threshold {Threshold}%", currentSpread, _minProfitThreshold);
                    return false;
                }
            }

            if (_strategy == ExecutionStrategy.Sequential)
            {
                return await ExecuteSequentialAsync(opportunity, buyClient, sellClient);
            }
            else
            {
                return await ExecuteConcurrentAsync(opportunity, buyClient, sellClient);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute trade");
            return false;
        }
    }

    private async Task<bool> ExecuteSequentialAsync(ArbitrageOpportunity opportunity, IExchangeClient buyClient, IExchangeClient sellClient)
    {
        // 1. Place Buy Order
        var buyResponse = await buyClient.PlaceMarketBuyOrderAsync(opportunity.Asset + "USD", opportunity.Volume);
        
        if (buyResponse.Status != OrderStatus.Filled && buyResponse.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogError("❌ Sequential Trade Failed: Buy order failed on {Exchange}. Error: {Error}", opportunity.BuyExchange, buyResponse.ErrorMessage);
            RecordTransaction(opportunity, buyResponse, null, "Failed");
            return false;
        }

        _logger.LogInformation("✅ Buy order filled on {Exchange}. Placing sell order on {SellEx}...", opportunity.BuyExchange, opportunity.SellExchange);

        // 2. Place Sell Order
        var sellResponse = await sellClient.PlaceMarketSellOrderAsync(opportunity.Asset + "USD", buyResponse.ExecutedQuantity);

        if (sellResponse.Status != OrderStatus.Filled && sellResponse.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogCritical("⚠️ CRITICAL: Buy order filled but Sell order FAILED on {Exchange}. Triggering UNDO logic...", opportunity.SellExchange);
            
            // 3. Recovery (Undo) Logic
            var undoResponse = await buyClient.PlaceMarketSellOrderAsync(opportunity.Asset + "USD", buyResponse.ExecutedQuantity);
            
            var status = undoResponse.Status == OrderStatus.Filled ? "Recovered" : "One-Sided Fill (CRITICAL)";
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, status);
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            transaction.RecoveryOrderId = undoResponse.OrderId;

            return false;
        }

        _logger.LogInformation("✅ Sequential trade completed successfully!");
        RecordTransaction(opportunity, buyResponse, sellResponse, "Success");
        return true;
    }

    private async Task<bool> ExecuteConcurrentAsync(ArbitrageOpportunity opportunity, IExchangeClient buyClient, IExchangeClient sellClient)
    {
        // Place both orders simultaneously
        var buyTask = buyClient.PlaceMarketBuyOrderAsync(opportunity.Asset + "USD", opportunity.Volume);
        var sellTask = sellClient.PlaceMarketSellOrderAsync(opportunity.Asset + "USD", opportunity.Volume);

        await Task.WhenAll(buyTask, sellTask);

        var buyResponse = await buyTask;
        var sellResponse = await sellTask;

        bool buySuccess = buyResponse.Status == OrderStatus.Filled || buyResponse.Status == OrderStatus.PartiallyFilled;
        bool sellSuccess = sellResponse.Status == OrderStatus.Filled || sellResponse.Status == OrderStatus.PartiallyFilled;

        if (buySuccess && sellSuccess)
        {
            _logger.LogInformation("✅ Concurrent trade completed successfully!");
            RecordTransaction(opportunity, buyResponse, sellResponse, "Success");
            return true;
        }

        // Handle one-sided failures
        if (buySuccess && !sellSuccess)
        {
            _logger.LogCritical("⚠️ Concurrent Trade One-Sided: Buy succeeded, Sell failed. Triggering UNDO on Buy exchange...");
            var undoResponse = await buyClient.PlaceMarketSellOrderAsync(opportunity.Asset + "USD", buyResponse.ExecutedQuantity);
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, "Recovered");
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            return false;
        }
        
        if (!buySuccess && sellSuccess)
        {
            _logger.LogCritical("⚠️ Concurrent Trade One-Sided: Sell succeeded, Buy failed. Triggering UNDO on Sell exchange...");
            var undoResponse = await sellClient.PlaceMarketBuyOrderAsync(opportunity.Asset + "USD", sellResponse.ExecutedQuantity);
            var transaction = RecordTransaction(opportunity, buyResponse, sellResponse, "Recovered");
            transaction.IsRecovered = undoResponse.Status == OrderStatus.Filled;
            return false;
        }

        _logger.LogError("❌ Concurrent Trade Failed: Both orders failed.");
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
        
        return transaction;
    }

    public List<Transaction> GetRecentTransactions()
    {
        return _transactions.Reverse().ToList();
    }
}
