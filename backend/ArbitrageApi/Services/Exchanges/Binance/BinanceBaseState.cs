using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public abstract class BinanceBaseState : IExchangeState
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;
    protected readonly string ApiKey;
    protected readonly string ApiSecret;
    protected readonly string BaseUrl;
    protected readonly string ExchangeName = "Binance";

    protected readonly Dictionary<string, string> SymbolMapping = new()
    {
        { "MATICUSDT", "POLUSDT" }
    };

    protected BinanceBaseState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret, string baseUrl)
    {
        HttpClient = httpClient;
        Logger = logger;
        ApiKey = apiKey;
        ApiSecret = apiSecret;
        BaseUrl = baseUrl;
        Logger.LogInformation("Binance ApiKey: {ApiKey}", ApiKey);
    }

    public virtual async Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        try
        {
            var apiSymbol = SymbolMapping.TryGetValue(symbol, out var mapped) ? mapped : symbol;
            var response = await HttpClient.GetFromJsonAsync<BinancePriceResponse>(
                $"{BaseUrl}/api/v3/ticker/price?symbol={apiSymbol}");

            if (response == null) return null;

            return new ExchangePrice
            {
                Exchange = ExchangeName,
                Symbol = symbol,
                Price = decimal.Parse(response.Price, System.Globalization.CultureInfo.InvariantCulture),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching price from Binance for {Symbol}", symbol);
            return null;
        }
    }

    public virtual async Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols)
    {
        var prices = new Dictionary<string, ExchangePrice>();
        try
        {
            var response = await HttpClient.GetFromJsonAsync<List<BinancePriceResponse>>(
                $"{BaseUrl}/api/v3/ticker/price");

            if (response == null) return prices;

            var priceLookup = response.ToDictionary(p => p.Symbol, p => p);

            foreach (var symbol in symbols)
            {
                var apiSymbol = SymbolMapping.TryGetValue(symbol, out var mapped) ? mapped : symbol;
                if (priceLookup.TryGetValue(apiSymbol, out var item))
                {
                    prices[symbol] = new ExchangePrice
                    {
                        Exchange = ExchangeName,
                        Symbol = symbol,
                        Price = decimal.Parse(item.Price, System.Globalization.CultureInfo.InvariantCulture),
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching prices from Binance");
        }
        return prices;
    }

    public abstract Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();
    public abstract Task<List<Balance>> GetBalancesAsync();

    public async Task UpdateSymbolMappingWithSupportedProductsAsync()
    {
        return;
    }
    public virtual async Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        try
        {
            var apiSymbol = SymbolMapping.TryGetValue(symbol, out var mapped) ? mapped : symbol;
            var url = $"{BaseUrl}/api/v3/depth?symbol={apiSymbol}&limit={limit}";
            var response = await HttpClient.GetFromJsonAsync<BinanceOrderBookResponse>(url);
            if (response == null) return null;
            var bids = response.Bids.Select(b => (decimal.Parse(b[0], System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(b[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            var asks = response.Asks.Select(a => (decimal.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            return (bids, asks);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching order book from Binance for {Symbol}", symbol);
            return null;
        }
    }

    protected class BinanceOrderBookResponse
    {
        [JsonPropertyName("bids")]
        public List<List<string>> Bids { get; set; } = new();
        [JsonPropertyName("asks")]
        public List<List<string>> Asks { get; set; } = new();
    }

    protected string Sign(string query)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ApiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(query));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    protected class BinancePriceResponse
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;
        [JsonPropertyName("price")]
        public string Price { get; set; } = string.Empty;
    }

    protected sealed class BinanceAccountResponse
    {
        [JsonPropertyName("commissionRates")]
        public BinanceCommissionRates CommissionRates { get; set; } = default!;
        [JsonPropertyName("balances")]
        public List<BinanceBalance> Balances { get; set; } = new();
    }

    protected sealed class BinanceCommissionRates
    {
        [JsonPropertyName("maker")]
        public decimal Maker { get; set; }
        [JsonPropertyName("taker")]
        public decimal Taker { get; set; }
    }

    protected sealed class BinanceBalance
    {
        [JsonPropertyName("asset")]
        public string Asset { get; set; } = string.Empty;
        [JsonPropertyName("free")]
        public string Free { get; set; } = string.Empty;
        [JsonPropertyName("locked")]
        public string Locked { get; set; } = string.Empty;
    }
    
    // Order placement methods
    public virtual async Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Buy, OrderType.Market, quantity, null);
    }
    
    public virtual async Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Sell, OrderType.Market, quantity, null);
    }
    
    public virtual async Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Buy, OrderType.Limit, quantity, price);
    }
    
    public virtual async Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Sell, OrderType.Limit, quantity, price);
    }
    
    protected virtual async Task<OrderResponse> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price)
    {
        try
        {
            var apiSymbol = SymbolMapping.TryGetValue(symbol, out var mapped) ? mapped : symbol;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            var parameters = new Dictionary<string, string>
            {
                { "symbol", apiSymbol },
                { "side", side == OrderSide.Buy ? "BUY" : "SELL" },
                { "type", type == OrderType.Market ? "MARKET" : "LIMIT" },
                { "quantity", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "timestamp", timestamp.ToString() }
            };
            
            if (type == OrderType.Limit && price.HasValue)
            {
                parameters.Add("price", price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                parameters.Add("timeInForce", "GTC"); // Good Till Cancel
            }
            
            var query = string.Join("&", parameters.Select(p => $"{p.Key}={p.Value}"));
            var signature = Sign(query);
            var fullQuery = $"{query}&signature={signature}";
            
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v3/order?{fullQuery}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            
            var response = await HttpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("Failed to place order on Binance: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new OrderResponse
                {
                    Status = Models.OrderStatus.Failed,
                    Symbol = symbol,
                    Type = type,
                    Side = side,
                    ErrorMessage = $"HTTP {response.StatusCode}: {responseContent}"
                };
            }
            
            var binanceOrder = await response.Content.ReadFromJsonAsync<BinanceOrderResponse>();
            if (binanceOrder == null)
            {
                return new OrderResponse
                {
                    Status = Models.OrderStatus.Failed,
                    Symbol = symbol,
                    Type = type,
                    Side = side,
                    ErrorMessage = "Failed to parse order response"
                };
            }
            
            Logger.LogInformation("✅ Order placed on Binance: {OrderId} - {Side} {Quantity} {Symbol} @ {Price}",
                binanceOrder.OrderId, side, quantity, symbol, price?.ToString() ?? "MARKET");
            
            return new OrderResponse
            {
                OrderId = binanceOrder.OrderId.ToString(),
                Symbol = symbol,
                Type = type,
                Side = side,
                Status = MapBinanceStatus(binanceOrder.Status),
                OriginalQuantity = decimal.Parse(binanceOrder.OrigQty, System.Globalization.CultureInfo.InvariantCulture),
                ExecutedQuantity = decimal.Parse(binanceOrder.ExecutedQty, System.Globalization.CultureInfo.InvariantCulture),
                Price = price,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(binanceOrder.TransactTime).DateTime
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error placing order on Binance");
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
    
    public virtual async Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"orderId={orderId}&timestamp={timestamp}";
            var signature = Sign(query);
            
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v3/order?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            
            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get order status: {response.StatusCode}");
            }
            
            var binanceOrder = await response.Content.ReadFromJsonAsync<BinanceOrderResponse>();
            if (binanceOrder == null)
            {
                throw new Exception("Failed to parse order status response");
            }
            
            return new OrderInfo
            {
                OrderId = binanceOrder.OrderId.ToString(),
                Symbol = binanceOrder.Symbol,
                Status = MapBinanceStatus(binanceOrder.Status),
                OriginalQuantity = decimal.Parse(binanceOrder.OrigQty, System.Globalization.CultureInfo.InvariantCulture),
                ExecutedQuantity = decimal.Parse(binanceOrder.ExecutedQty, System.Globalization.CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(binanceOrder.Time).DateTime
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting order status from Binance");
            throw;
        }
    }
    
    public virtual async Task<bool> CancelOrderAsync(string orderId)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"orderId={orderId}&timestamp={timestamp}";
            var signature = Sign(query);
            
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/v3/order?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);
            
            var response = await HttpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformation("✅ Order cancelled on Binance: {OrderId}", orderId);
                return true;
            }
            
            Logger.LogWarning("Failed to cancel order on Binance: {OrderId} - {StatusCode}", orderId, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error cancelling order on Binance");
            return false;
        }
    }
    
    private Models.OrderStatus MapBinanceStatus(string binanceStatus)
    {
        return binanceStatus switch
        {
            "NEW" => Models.OrderStatus.Pending,
            "PARTIALLY_FILLED" => Models.OrderStatus.PartiallyFilled,
            "FILLED" => Models.OrderStatus.Filled,
            "CANCELED" => Models.OrderStatus.Cancelled,
            "REJECTED" => Models.OrderStatus.Rejected,
            "EXPIRED" => Models.OrderStatus.Cancelled,
            _ => Models.OrderStatus.Failed
        };
    }
    
    protected class BinanceOrderResponse
    {
        [JsonPropertyName("orderId")]
        public long OrderId { get; set; }
        
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;
        
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonPropertyName("origQty")]
        public string OrigQty { get; set; } = "0";
        
        [JsonPropertyName("executedQty")]
        public string ExecutedQty { get; set; } = "0";
        
        [JsonPropertyName("transactTime")]
        public long TransactTime { get; set; }
        
        [JsonPropertyName("time")]
        public long Time { get; set; }
    }
    
    // Sandbox management
    public abstract Task DepositSandboxFundsAsync(string asset, decimal amount);
}
