using System.Net.Http.Json;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges.Coinbase;

namespace ArbitrageApi.Services.Exchanges;

public class CoinbaseSandboxState : CoinbaseBaseState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _balances = new();

    public CoinbaseSandboxState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret) 
        : base(httpClient, logger, apiKey, apiSecret, "https://api-public.sandbox.exchange.coinbase.com")
    {
        // Initialize with default funds
        _balances["USD"] = 10000m;
        _balances["BTC"] = 0.5m;
        _balances["ETH"] = 5.0m;
        _balances["BNB"] = 50.0m;
        _balances["SOL"] = 100.0m;
        _balances["XRP"] = 5000.0m;
        _balances["ADA"] = 10000.0m;
        _balances["AVAX"] = 100.0m;
        _balances["DOT"] = 500.0m;
        _balances["MATIC"] = 5000.0m;
        _balances["LINK"] = 200.0m;
    }

    public override Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        // FULL ISOLATION: Return simulated price immediately without network calls
        return Task.FromResult(GetSimulatedPrice(symbol));
    }

    private ExchangePrice? GetSimulatedPrice(string symbol)
    {
        // Fallback to a reasonable simulated price if the sandbox API is down
        decimal price = symbol switch
        {
            "BTCUSDT" => 50000m,
            "ETHUSDT" => 2500m,
            "BNBUSDT" => 300m,
            "SOLUSDT" => 100m,
            "XRPUSDT" => 0.5m,
            "ADAUSDT" => 0.5m,
            "AVAXUSDT" => 35m,
            "DOTUSDT" => 7m,
            "MATICUSDT" => 0.8m,
            "LINKUSDT" => 15m,
            _ => 10m
        };

        return new ExchangePrice
        {
            Exchange = ExchangeName,
            Symbol = symbol,
            Price = price,
            Timestamp = DateTime.UtcNow
        };
    }

    public override async Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        try
        {
            var requestPath = "/fees";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = Sign(timestamp, "GET", requestPath, "", true);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{requestPath}");
            request.Headers.Add("CB-ACCESS-KEY", ApiKey);
            request.Headers.Add("CB-ACCESS-SIGN", signature);
            request.Headers.Add("CB-ACCESS-TIMESTAMP", timestamp);

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (0.005m, 0.005m);

            var fees = await response.Content.ReadFromJsonAsync<CoinbaseExchangeFeeResponse>();
            return (fees?.MakerFeeRate ?? 0.005m, fees?.TakerFeeRate ?? 0.005m);
        }
        catch
        {
            return (0.005m, 0.005m);
        }
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

    public override async Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        Logger.LogInformation("SANDBOX: Placing market buy order for {Symbol}, quantity {Quantity}", symbol, quantity);
        
        var orderId = $"SANDBOX_CB_BUY_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var priceInfo = await GetPriceAsync(symbol);
        var price = priceInfo?.Price ?? 0m;
        var totalCost = quantity * price;

        if (!_balances.TryGetValue("USD", out var usdBalance) || usdBalance < totalCost)
        {
            Logger.LogWarning("❌ SANDBOX: Insufficient USD balance for {Symbol} buy. Need {Cost}, have {Have}", symbol, totalCost, usdBalance);
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = "Insufficient USD balance" };
        }

        _balances["USD"] -= totalCost;
        var asset = symbol.Replace("-USD", "").Replace("USDT", "").Replace("USD", "");
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
        var asset = symbol.Replace("-USD", "").Replace("USDT", "").Replace("USD", "");

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
        var asset = symbol.Replace("-USD", "").Replace("USDT", "");
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
        var asset = symbol.Replace("-USD", "").Replace("USDT", "");

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

    public override async Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        Logger.LogInformation("🎮 SANDBOX: Providing simulated order book for {Symbol}", symbol);
        
        var priceInfo = await GetPriceAsync(symbol);
        var midPrice = priceInfo?.Price ?? 50000m; // Fallback if price fetch fails

        return GetSimulatedOrderBook(midPrice, limit);
    }

    public override async Task<bool> CancelOrderAsync(string orderId)
    {
        Logger.LogInformation("SANDBOX: Cancelling order {OrderId}", orderId);
        return true;
    }
}
