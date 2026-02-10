using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class BinanceSandboxState : BinanceBaseState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _balances = new();
    private readonly IExchangeState _realState;

    public BinanceSandboxState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret, IExchangeState realState) 
        : base(httpClient, logger, apiKey, apiSecret, "https://testnet.binance.vision")
    {
        _realState = realState;

        // Initialize with default funds
        _balances["USD"] = 100000m;    // Backend Logic uses this
        _balances["USDT"] = 100000m;   // Frontend Logic (QuoteAsset) likely looks for this
        _balances["BTC"] = 10m;
        _balances["ETH"] = 100m;
        _balances["BNB"] = 1000m;
        _balances["SOL"] = 1000m;
        _balances["XRP"] = 10000m;
        _balances["ADA"] = 10000m;
        _balances["AVAX"] = 1000m;
        _balances["DOT"] = 1000m;
        _balances["MATIC"] = 10000m;
        _balances["LINK"] = 1000m;
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

    public override Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null)
    {
        Logger.LogInformation("🧪 [Sandbox] Mock Withdrawal of {Amount} {Asset} to {Address} (Network: {Network})", amount, asset, address, network ?? "Default");
        return Task.FromResult($"mock_tx_{Guid.NewGuid()}");
    }

    public override System.Threading.Tasks.Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        var mockAddr = $"SANDBOX_BINANCE_{asset.ToUpper()}";
        Logger.LogInformation("🧪 [Sandbox] Mock Deposit Address for {Asset}: {Address}", asset, mockAddr);
        return Task.FromResult<string?>(mockAddr);
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
            
            // Use Real Price for execution math
            var priceInfo = await GetPriceAsync(symbol);
            var currentPrice = price ?? priceInfo?.Price ?? 0m;

            if (currentPrice <= 0)
            {
                return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Could not fetch real price for execution" };
            }

            // Robust asset parsing
            var asset = symbol.Contains("-") ? symbol.Split('-')[0] : symbol.Replace("USDT", "").Replace("USD", "");

            if (side == OrderSide.Buy)
            {
                var totalCost = quantity * currentPrice;
                if (!_balances.TryGetValue("USD", out var usdBalance) || usdBalance < totalCost)
                {
                    Logger.LogWarning("❌ SANDBOX: Insufficient USD balance for {Symbol} buy. Need {Cost}, have {Have}", symbol, totalCost, usdBalance);
                    return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Insufficient USD balance" };
                }
                _balances["USD"] -= totalCost;
                _balances.AddOrUpdate(asset, quantity, (_, old) => old + quantity);

                File.AppendAllText("trade_debug.log", $"[{DateTime.UtcNow:HH:mm:ss}] SANDBOX BINANCE BUY: Filled {quantity} {asset} at {currentPrice}. Cost: {totalCost}. New USD: {_balances["USD"]}\n");
            }
            else
            {
                if (!_balances.TryGetValue(asset, out var assetBalance) || assetBalance < quantity)
                {
                    Logger.LogWarning("❌ SANDBOX: Insufficient {Asset} balance for {Symbol} sell. Need {Qty}, have {Have}", asset, symbol, quantity, assetBalance);
                    return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = $"Insufficient {asset} balance" };
                }
                _balances[asset] -= quantity;
                var totalProceeds = quantity * currentPrice;
                _balances.AddOrUpdate("USD", totalProceeds, (_, old) => old + totalProceeds);

                File.AppendAllText("trade_debug.log", $"[{DateTime.UtcNow:HH:mm:ss}] SANDBOX BINANCE SELL: Filled {quantity} {asset} at {currentPrice}. Proceeds: {totalProceeds}. New USD: {_balances["USD"]}\n");
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
    
    public override Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        // FULL ISOLATION: Return REAL price 
        return _realState.GetPriceAsync(symbol);
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
    
    public override Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        Logger.LogInformation("🎮 SANDBOX: Providing REAL order book for {Symbol}", symbol);
        return _realState.GetOrderBookAsync(symbol, limit);
    }

    public override Task<bool> CancelOrderAsync(string orderId)
    {
        return Task.FromResult(true);
    }
}
