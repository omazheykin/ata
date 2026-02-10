using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace ArbitrageApi.Services;

public class OrderExecutionService
{
    private readonly ILogger<OrderExecutionService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ChannelProvider _channelProvider;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> _hubContext;
    private readonly ConcurrentQueue<Transaction> _transactions = new();
    
    // Configurable via TradeService, but we'll default here
    private ExecutionStrategy _strategy = ExecutionStrategy.Sequential;

    public OrderExecutionService(
        ILogger<OrderExecutionService> logger,
        IEnumerable<IExchangeClient> exchangeClients,
        ChannelProvider channelProvider,
        Microsoft.AspNetCore.SignalR.IHubContext<ArbitrageApi.Hubs.ArbitrageHub> hubContext)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
        _channelProvider = channelProvider;
        _hubContext = hubContext;
    }

    public void SetExecutionStrategy(ExecutionStrategy strategy)
    {
        _strategy = strategy;
        _logger.LogInformation("Execution Strategy set to {Strategy}", strategy);
    }
    
    public ExecutionStrategy GetExecutionStrategy() => _strategy;

    public virtual async Task<bool> ExecuteTradeAsync(ArbitrageOpportunity opportunity, decimal minProfitThreshold, CancellationToken ct = default)
    {
        try
        {
            // [DIAGNOSTIC] Log trade attempt
            File.AppendAllText("trade_debug.log", $"[{DateTime.UtcNow:HH:mm:ss}] Attempting trade: {opportunity.Symbol}, Buy on {opportunity.BuyExchange}, Sell on {opportunity.SellExchange}, Vol: {opportunity.Volume}\n");

            _logger.LogInformation("🚀 Executing trade for {Asset} using {Strategy} strategy...", opportunity.Asset, _strategy);

            var buyClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName.Equals(opportunity.BuyExchange, StringComparison.OrdinalIgnoreCase));
            var sellClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName.Equals(opportunity.SellExchange, StringComparison.OrdinalIgnoreCase));

            if (buyClient == null || sellClient == null)
            {
                File.AppendAllText("trade_debug.log", $"[{DateTime.UtcNow:HH:mm:ss}] ERROR: Missing exchange client. BuyEx: {opportunity.BuyExchange} ({(buyClient == null ? "NULL" : "OK")}), SellEx: {opportunity.SellExchange} ({(sellClient == null ? "NULL" : "OK")})\n");
                _logger.LogError("Missing exchange client for trade execution.");
                return false;
            }

            // 1. Slippage Check (Re-verify price immediately before execution)
            var currentBuyPrice = await buyClient.GetPriceAsync(opportunity.Symbol);
            var currentSellPrice = await sellClient.GetPriceAsync(opportunity.Symbol);

            if (currentBuyPrice != null && currentSellPrice != null)
            {
                var currentSpread = ((currentSellPrice.Price - currentBuyPrice.Price) / currentBuyPrice.Price) * 100;
                
                // Allow a small buffer below the expected threshold for slippage, but not negative
                // For Passive Rebalancing, minProfitThreshold might be 0.01%
                if (currentSpread < minProfitThreshold)
                {
                    _logger.LogWarning("⚠️ Trade aborted: Slippage exceeded. Current spread {Spread:N2}% < Threshold {Threshold}%", currentSpread, minProfitThreshold);
                    return false;
                }
                _logger.LogInformation("✅ Slippage check passed: Current spread {Spread:N2}% >= Threshold {Threshold}%", currentSpread, minProfitThreshold);
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
        // PnL Calculation
        decimal buyPrice = buy.AveragePrice ?? buy.Price ?? 0m;
        decimal buyQty = buy.ExecutedQuantity;
        decimal buyCost = buyPrice * buyQty;

        decimal sellPrice = sell?.AveragePrice ?? sell?.Price ?? 0m;
        decimal sellQty = sell?.ExecutedQuantity ?? 0m;
        decimal sellProceeds = sellPrice * sellQty;

        // Estimate fees if not provided by exchange (0.1% taker fee)
        decimal buyFee = buy.Fee > 0 ? buy.Fee : (buyCost * 0.001m);
        decimal sellFee = (sell?.Fee ?? 0) > 0 ? sell!.Fee : (sellProceeds * 0.001m);
        decimal totalFees = buyFee + sellFee;

        // Realized Profit = (Sell Proceeds - Buy Cost) - Total Fees
        decimal realizedProfit = 0m;
        if (status == "Success" && sell != null)
        {
            realizedProfit = (sellProceeds - buyCost) - totalFees;
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "Arbitrage",
            Asset = opportunity.Asset,
            Amount = buy.ExecutedQuantity,
            Exchange = $"{opportunity.BuyExchange} → {opportunity.SellExchange}",
            Price = buyPrice,
            Fee = totalFees, // Legacy Fee field (Projected/Estimated)
            Profit = (sellPrice - buyPrice) * buyQty, // Legacy Profit field (Gross / Projected)
            Status = status,
            
            // New PnL Fields
            RealizedProfit = realizedProfit,
            TotalFees = totalFees,
            BuyCost = buyCost,
            SellProceeds = sellProceeds,

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
