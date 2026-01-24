using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class BinanceSandboxState : BinanceBaseState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _balances = new();

    public BinanceSandboxState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret) 
        : base(httpClient, logger, apiKey, apiSecret, "https://testnet.binance.vision")
    {
        // Initialize with default funds
        _balances["USD"] = 10000m;
        _balances["BTC"] = 0.5m;
        _balances["ETH"] = 5.0m;
    }

    public override Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        return Task.FromResult<(decimal Maker, decimal Taker)?>((0.001m, 0.001m));
    }

    public override Task<List<Balance>> GetBalancesAsync()
    {
        var result = _balances.Select(kvp => new Balance
        {
            Asset = kvp.Key,
            Free = kvp.Value,
            Locked = 0
        }).ToList();
        return Task.FromResult(result);
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        _balances.AddOrUpdate(asset, amount, (_, old) => old + amount);
        Logger.LogInformation("💰 SANDBOX DEPOSIT: {Amount} {Asset} to {Exchange}", amount, asset, ExchangeName);
        return Task.CompletedTask;
    }
    
    protected override async Task<OrderResponse> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price)
    {
        try
        {
            Logger.LogInformation("🎮 SANDBOX: Simulating {Type} {Side} order for {Quantity} {Symbol}",
                type, side, quantity, symbol);
            
            await Task.Delay(100);
            
            var orderId = $"SANDBOX_{Guid.NewGuid().ToString().Substring(0, 8)}";
            
            // Simple price discovery for balance updates
            var priceInfo = await GetPriceAsync(symbol);
            var currentPrice = price ?? priceInfo?.Price ?? 0m;

            if (side == OrderSide.Buy)
            {
                var totalCost = quantity * currentPrice;
                if (!_balances.TryGetValue("USD", out var usdBalance) || usdBalance < totalCost)
                {
                    return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Insufficient USD balance" };
                }
                _balances["USD"] -= totalCost;
                var asset = symbol.Replace("USDT", "").Replace("USD", "");
                _balances.AddOrUpdate(asset, quantity, (_, old) => old + quantity);
            }
            else
            {
                var asset = symbol.Replace("USDT", "").Replace("USD", "");
                if (!_balances.TryGetValue(asset, out var assetBalance) || assetBalance < quantity)
                {
                    return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = $"Insufficient {asset} balance" };
                }
                _balances[asset] -= quantity;
                var totalProceeds = quantity * currentPrice;
                _balances.AddOrUpdate("USD", totalProceeds, (_, old) => old + totalProceeds);
            }

            Logger.LogInformation("✅ SANDBOX: Order simulated successfully: {OrderId}", orderId);
            
            return new OrderResponse
            {
                OrderId = orderId,
                Symbol = symbol,
                Type = type,
                Side = side,
                Status = Models.OrderStatus.Filled,
                OriginalQuantity = quantity,
                ExecutedQuantity = quantity,
                Price = currentPrice,
                CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error simulating order in sandbox");
            return new OrderResponse
            {
                Status = Models.OrderStatus.Failed,
                Symbol = symbol,
                Type = type,
                Side = side,
                ErrorMessage = ex.Message
            };
        }
    }
    
    public override Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        return Task.FromResult(new OrderInfo
        {
            OrderId = orderId,
            Symbol = "BTCUSDT",
            Status = Models.OrderStatus.Filled,
            OriginalQuantity = 0.001m,
            ExecutedQuantity = 0.001m,
            CreatedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow
        });
    }
    
    public override Task<bool> CancelOrderAsync(string orderId)
    {
        return Task.FromResult(true);
    }
}
