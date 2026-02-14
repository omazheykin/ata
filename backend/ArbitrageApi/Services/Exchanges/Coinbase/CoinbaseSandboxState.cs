using System.Net.Http.Json;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges.Coinbase;

namespace ArbitrageApi.Services.Exchanges;

public class CoinbaseSandboxState : CoinbaseBaseState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _balances = new();

    private readonly IExchangeState _realState;

    public CoinbaseSandboxState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret, IExchangeState realState) 
        : base(httpClient, logger, apiKey, apiSecret, "https://api-public.sandbox.exchange.coinbase.com")
    {
        _realState = realState;
        
        // Initialize with default funds
        _balances["USD"] = 100000m;
        _balances["BTC"] = 10m; // 10 BTC is plenty
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

    public override Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        // Use REAL price for simulation accuracy
        return _realState.GetPriceAsync(symbol);
    }

    public override Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        // Sandbox typically has standard low fees
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
        Logger.LogInformation("🧪 [Sandbox] Mock Coinbase Withdrawal of {Amount} {Asset} to {Address}", amount, asset, address);
        return Task.FromResult($"mock_cb_tx_{Guid.NewGuid()}");
    }

    public override System.Threading.Tasks.Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        var mockAddr = $"SANDBOX_COINBASE_{asset.ToUpper()}";
        Logger.LogInformation("🧪 [Sandbox] Mock Coinbase Deposit Address for {Asset}: {Address}", asset, mockAddr);
        return Task.FromResult<string?>(mockAddr);
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        _balances.AddOrUpdate(asset, amount, (_, old) => old + amount);
        Logger.LogInformation("💰 SANDBOX DEPOSIT: {Amount} {Asset} to {Exchange}", amount, asset, ExchangeName);
        return Task.CompletedTask;
    }

    public override async Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        Logger.LogInformation("SANDBOX: Placing market buy order for {Symbol}, quantity {Quantity}", symbol, quantity);
        
        var orderId = $"SANDBOX_CB_BUY_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        
        // Use real price for execution math
        var priceInfo = await GetPriceAsync(symbol);
        var price = priceInfo?.Price ?? 0m;
        
        if (price <= 0) 
        {
             Logger.LogError("❌ SANDBOX: Could not fetch real price for {Symbol}. trade aborted.", symbol);
             return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Could not fetch price" };
        }

        var totalCost = quantity * price;

        if (!_balances.TryGetValue("USD", out var usdBalance) || usdBalance < totalCost)
        {
            Logger.LogWarning("❌ SANDBOX: Insufficient USD balance for {Symbol} buy. Need {Cost}, have {Have}", symbol, totalCost, usdBalance);
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Insufficient USD balance" };
        }

        _balances["USD"] -= totalCost;
        
        // Robust asset parsing: "BTC-USD" -> "BTC", "BTCUSDT" -> "BTC"
        var asset = symbol.Contains("-") ? symbol.Split('-')[0] : symbol.Replace("USDT", "").Replace("USD", "");
        
        _balances.AddOrUpdate(asset, quantity, (_, old) => old + quantity);


        return new OrderResponse
        {
            OrderId = orderId,
            Symbol = symbol,
            Status = OrderStatus.Filled,
            Type = OrderType.Market,
            Side = OrderSide.Buy,
            OriginalQuantity = quantity,
            ExecutedQuantity = quantity,
            Price = price,
            Timestamp = DateTime.UtcNow
        };
    }

    public override async Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        Logger.LogInformation("SANDBOX: Placing market sell order for {Symbol}, quantity {Quantity}", symbol, quantity);
        
        var orderId = $"SANDBOX_CB_SELL_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var asset = symbol.Contains("-") ? symbol.Split('-')[0] : symbol.Replace("USDT", "").Replace("USD", "");

        if (!_balances.TryGetValue(asset, out var assetBalance) || assetBalance < quantity)
        {
            Logger.LogWarning("❌ SANDBOX: Insufficient {Asset} balance for {Symbol} sell. Need {Qty}, have {Have}", asset, symbol, quantity, assetBalance);
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = $"Insufficient {asset} balance" };
        }

        _balances[asset] -= quantity;
        
        var priceInfo = await GetPriceAsync(symbol);
        var price = priceInfo?.Price ?? 0m;
        
        var totalProceeds = quantity * price;
        _balances.AddOrUpdate("USD", totalProceeds, (_, old) => old + totalProceeds);


        return new OrderResponse
        {
            OrderId = orderId,
            Symbol = symbol,
            Status = OrderStatus.Filled,
            Type = OrderType.Market,
            Side = OrderSide.Sell,
            OriginalQuantity = quantity,
            ExecutedQuantity = quantity,
            Price = price,
            Timestamp = DateTime.UtcNow
        };
    }

    public override async Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        Logger.LogInformation("SANDBOX: Placing limit buy order for {Symbol}, quantity {Quantity}, price {Price}", symbol, quantity, price);
        
        var orderId = $"SANDBOX_CB_LBUY_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var totalCost = quantity * price;

        if (!_balances.TryGetValue("USD", out var usdBalance) || usdBalance < totalCost)
        {
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Insufficient USD balance" };
        }

        _balances["USD"] -= totalCost;
        var asset = symbol.Contains("-") ? symbol.Split('-')[0] : symbol.Replace("USDT", "").Replace("USD", "");
        _balances.AddOrUpdate(asset, quantity, (_, old) => old + quantity);


        return new OrderResponse
        {
            OrderId = orderId,
            Symbol = symbol,
            Status = OrderStatus.Filled,
            Type = OrderType.Limit,
            Side = OrderSide.Buy,
            OriginalQuantity = quantity,
            ExecutedQuantity = quantity,
            Price = price,
            Timestamp = DateTime.UtcNow
        };
    }

    public override async Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        Logger.LogInformation("SANDBOX: Placing limit sell order for {Symbol}, quantity {Quantity}, price {Price}", symbol, quantity, price);
        
        var orderId = $"SANDBOX_CB_LSELL_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var asset = symbol.Contains("-") ? symbol.Split('-')[0] : symbol.Replace("USDT", "").Replace("USD", "");

        if (!_balances.TryGetValue(asset, out var assetBalance) || assetBalance < quantity)
        {
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = $"Insufficient {asset} balance" };
        }

        _balances[asset] -= quantity;
        var totalProceeds = quantity * price;
        _balances.AddOrUpdate("USD", totalProceeds, (_, old) => old + totalProceeds);


        return new OrderResponse
        {
            OrderId = orderId,
            Symbol = symbol,
            Status = OrderStatus.Filled,
            Type = OrderType.Limit,
            Side = OrderSide.Sell,
            OriginalQuantity = quantity,
            ExecutedQuantity = quantity,
            Price = price,
            Timestamp = DateTime.UtcNow
        };
    }

    public override async Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        return new OrderInfo
        {
            OrderId = orderId,
            Status = OrderStatus.Filled,
            OriginalQuantity = 1.0m,
            ExecutedQuantity = 1.0m,
            Timestamp = DateTime.UtcNow
        };
    }

    public override Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        // Delegate to REAL state for real order book data
        return _realState.GetOrderBookAsync(symbol, limit);
    }

    public override async Task<bool> CancelOrderAsync(string orderId)
    {
        Logger.LogInformation("SANDBOX: Cancelling order {OrderId}", orderId);
        return true;
    }
}
