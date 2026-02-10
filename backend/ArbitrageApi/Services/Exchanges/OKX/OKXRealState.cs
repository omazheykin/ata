using ArbitrageApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace ArbitrageApi.Services.Exchanges.OKX;

public class OKXRealState : OKXBaseState
{
    public OKXRealState(
        HttpClient httpClient,
        ILogger<OKXRealState> logger,
        string apiKey,
        string secretKey,
        string passphrase,
        string baseUrl)
        : base(httpClient, logger, apiKey, secretKey, passphrase, baseUrl)
    {
    }

    public override async Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        try
        {
            var path = "/api/v5/account/trade-fee?instType=SPOT";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
            AddOKXHeaders(request, timestamp, "GET", path, "");

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("OKX fee API returned {StatusCode}", response.StatusCode);
                return (0.0008m, 0.001m);
            }

            var feeResponse = await response.Content.ReadFromJsonAsync<OKXFeeResponse>();
            if (feeResponse?.Data != null && feeResponse.Data.Count > 0)
            {
                var feeData = feeResponse.Data[0];
                return (decimal.Parse(feeData.Maker), decimal.Parse(feeData.Taker));
            }

            return (0.0008m, 0.001m);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching OKX fees");
            return (0.0008m, 0.001m);
        }
    }

    public override async Task<Dictionary<string, ExchangePrice>> GetPricesAsync(string[] symbols)
    {
        var prices = new Dictionary<string, ExchangePrice>();
        try
        {
            // OKX uses /api/v5/market/tickers?instType=SPOT
            var path = "/api/v5/market/tickers?instType=SPOT";
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("OKX price API returned {StatusCode}", response.StatusCode);
                return prices;
            }

            var tickerResponse = await response.Content.ReadFromJsonAsync<OKXTickerResponse>();
            if (tickerResponse?.Data == null) return prices;

            // Create a lookup by OKX symbol
            var tickerLookup = tickerResponse.Data.ToDictionary(t => t.InstId, t => t);

            foreach (var symbol in symbols)
            {
                var pair = TradingPair.CommonPairs.FirstOrDefault(p => p.Symbol == symbol);
                if (pair != null)
                {
                    var okxSymbol = pair.GetOKXSymbol();
                    if (tickerLookup.TryGetValue(okxSymbol, out var ticker))
                    {
                        prices[symbol] = new ExchangePrice
                        {
                            Exchange = ExchangeName,
                            Symbol = symbol,
                            Price = decimal.Parse(ticker.Last),
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching prices from OKX");
        }
        return prices;
    }

    public override async Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        try
        {
            var pair = TradingPair.CommonPairs.FirstOrDefault(p => p.Symbol == symbol);
            if (pair == null) return null;

            var okxSymbol = pair.GetOKXSymbol();
            var path = $"/api/v5/market/books?instId={okxSymbol}&sz={limit}";

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) return null;

            var bookResponse = await response.Content.ReadFromJsonAsync<OKXOrderBookResponse>();
            if (bookResponse?.Data == null || bookResponse.Data.Count == 0) return null;

            var bookData = bookResponse.Data[0];
            var bids = bookData.Bids.Select(b => (decimal.Parse(b[0]), decimal.Parse(b[1]))).ToList();
            var asks = bookData.Asks.Select(a => (decimal.Parse(a[0]), decimal.Parse(a[1]))).ToList();

            return (bids, asks);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching order book from OKX for {Symbol}", symbol);
            return null;
        }
    }

    public override Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null)
    {
        // POST /api/v5/asset/withdrawal (Mock for now)
        throw new NotImplementedException("OKX real withdrawals not yet enabled for safety.");
    }

    public override async Task<List<Balance>> GetBalancesAsync()
    {
        try
        {
            var path = "/api/v5/account/balance";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
            AddOKXHeaders(request, timestamp, "GET", path, "");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) 
            {
                Logger.LogWarning("OKX balance API returned {StatusCode}, using cache.", response.StatusCode);
                return CachedBalances;
            }

            var balanceResponse = await response.Content.ReadFromJsonAsync<OKXBalanceResponse>();
            if (balanceResponse?.Data == null || balanceResponse.Data.Count == 0) 
            {
                Logger.LogWarning("OKX balance response data is empty, using cache.");
                return CachedBalances;
            }

            var freshBalances = new List<Balance>();
            foreach (var detail in balanceResponse.Data[0].Details)
            {
                var free = decimal.Parse(detail.AvailBal, System.Globalization.CultureInfo.InvariantCulture);
                var locked = decimal.Parse(detail.FrozenBal, System.Globalization.CultureInfo.InvariantCulture);
                
                if (free > 0 || locked > 0)
                {
                    freshBalances.Add(new Balance
                    {
                        Asset = detail.Ccy,
                        Free = free,
                        Locked = locked
                    });
                }
            }

            if (freshBalances.Any())
            {
                CachedBalances = freshBalances;
                LastBalanceUpdate = DateTime.UtcNow;
            }

            return freshBalances;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching balances from OKX, returning cache.");
            return CachedBalances;
        }
    }

    public override async Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        try
        {
            var path = $"/api/v5/asset/deposit-address?ccy={asset.ToUpper()}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
            AddOKXHeaders(request, timestamp, "GET", path, "");

            var response = await HttpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                return data[0].TryGetProperty("addr", out var addr) ? addr.GetString() : null;
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching OKX deposit address for {Asset}", asset);
            return null;
        }
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        // No-op for real state
        return Task.CompletedTask;
    }

    // Order methods
    public override async Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, "buy", "market", quantity, null);
    }

    public override async Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, "sell", "market", quantity, null);
    }

    public override async Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, "buy", "limit", quantity, price);
    }

    public override async Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, "sell", "limit", quantity, price);
    }

    private async Task<OrderResponse> PlaceOrderAsync(string symbol, string side, string type, decimal quantity, decimal? price)
    {
        try
        {
            var pair = TradingPair.CommonPairs.FirstOrDefault(p => p.Symbol == symbol);
            var okxSymbol = pair?.GetOKXSymbol() ?? symbol;
            
            var path = "/api/v5/trade/order";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            
            var orderRequest = new
            {
                instId = okxSymbol,
                tdMode = "cash",
                side = side,
                ordType = type,
                sz = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                px = price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                // Target currency: for market buy, we want to specify base currency quantity
                tgtCcy = type == "market" && side == "buy" ? "base_ccy" : null
            };

            var body = JsonSerializer.Serialize(orderRequest);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            AddOKXHeaders(request, timestamp, "POST", path, body);

            var response = await HttpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("OKX Order failed: {StatusCode} - {Content}", response.StatusCode, content);
                return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = content };
            }

            var okxResponse = JsonSerializer.Deserialize<OKXOrderResponse>(content);
            if (okxResponse?.Code != "0" || okxResponse.Data == null || okxResponse.Data.Count == 0)
            {
                var msg = okxResponse?.Msg ?? "Unknown OKX error";
                Logger.LogError("OKX Order error: {Code} - {Msg}", okxResponse?.Code, msg);
                return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = msg };
            }

            var orderData = okxResponse.Data[0];
            Logger.LogInformation("✅ OKX Order placed: {OrdId}", orderData.OrdId);

            return new OrderResponse
            {
                OrderId = orderData.OrdId,
                Symbol = symbol,
                Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = type == "market" ? OrderType.Market : OrderType.Limit,
                Status = Models.OrderStatus.Pending, // Initial status
                OriginalQuantity = quantity,
                Price = price,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error placing OKX order for {Symbol}", symbol);
            return new OrderResponse { Status = Models.OrderStatus.Failed, ErrorMessage = ex.Message };
        }
    }

    public override async Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        try
        {
            var path = $"/api/v5/trade/order?ordId={orderId}"; // Actually documentation says query params or body
            // Wait, for GET it should be query params.
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
            AddOKXHeaders(request, timestamp, "GET", path, "");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new Exception($"HTTP {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var okxResponse = JsonSerializer.Deserialize<OKXOrderDetailResponse>(content);

            if (okxResponse?.Code != "0" || okxResponse.Data == null || okxResponse.Data.Count == 0)
                throw new Exception(okxResponse?.Msg ?? "Order not found");

            var data = okxResponse.Data[0];
            return new OrderInfo
            {
                OrderId = data.OrdId,
                Symbol = data.InstId,
                Status = MapOKXStatus(data.State),
                OriginalQuantity = decimal.Parse(data.Sz),
                ExecutedQuantity = decimal.Parse(data.AccFillSz),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data.UTime)).DateTime
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting OKX order status for {OrderId}", orderId);
            throw;
        }
    }

    public override async Task<bool> CancelOrderAsync(string orderId)
    {
        try
        {
            // OKX Cancel requires instId. We might need to store it or look it up.
            // For now, let's assume we can find it or use a separate endpoint if available.
            // The /api/v5/trade/cancel-order needs instId.
            // Simplified: we'll use a placeholder/fail if we don't have instId, 
            // but usually we can try to guess from the OrderInfo if we fetch it first.
            
            // To be more robust, we should probably pass symbol to CancelOrderAsync.
            // But IExchangeClient doesn't have it.
            return false; 
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error cancelling OKX order {OrderId}", orderId);
            return false;
        }
    }

    private Models.OrderStatus MapOKXStatus(string state)
    {
        return state switch
        {
            "live" => Models.OrderStatus.Pending,
            "partially_filled" => Models.OrderStatus.PartiallyFilled,
            "filled" => Models.OrderStatus.Filled,
            "canceled" => Models.OrderStatus.Cancelled,
            "order_failed" => Models.OrderStatus.Failed,
            _ => Models.OrderStatus.Failed
        };
    }
}

public class OKXOrderResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
    [JsonPropertyName("data")]
    public List<OKXOrderData>? Data { get; set; }
}

public class OKXOrderData
{
    [JsonPropertyName("ordId")]
    public string OrdId { get; set; } = string.Empty;
    [JsonPropertyName("clOrdId")]
    public string ClOrdId { get; set; } = string.Empty;
}

public class OKXOrderDetailResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
    [JsonPropertyName("data")]
    public List<OKXOrderDetail>? Data { get; set; }
}

public class OKXOrderDetail
{
    [JsonPropertyName("instId")]
    public string InstId { get; set; } = string.Empty;
    [JsonPropertyName("ordId")]
    public string OrdId { get; set; } = string.Empty;
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("sz")]
    public string Sz { get; set; } = string.Empty;
    [JsonPropertyName("accFillSz")]
    public string AccFillSz { get; set; } = string.Empty;
    [JsonPropertyName("uTime")]
    public string UTime { get; set; } = string.Empty;
}

// Response models
public class OKXFeeResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<OKXFeeData> Data { get; set; } = new();
}

public class OKXFeeData
{
    [JsonPropertyName("maker")]
    public string Maker { get; set; } = string.Empty;

    [JsonPropertyName("taker")]
    public string Taker { get; set; } = string.Empty;
}

public class OKXTickerResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<OKXTicker> Data { get; set; } = new();
}

public class OKXTicker
{
    [JsonPropertyName("instId")]
    public string InstId { get; set; } = string.Empty;

    [JsonPropertyName("last")]
    public string Last { get; set; } = string.Empty;
}

public class OKXOrderBookResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<OKXOrderBookData> Data { get; set; } = new();
}

public class OKXOrderBookData
{
    [JsonPropertyName("bids")]
    public List<string[]> Bids { get; set; } = new();

    [JsonPropertyName("asks")]
    public List<string[]> Asks { get; set; } = new();
}

public class OKXBalanceResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<OKXAccountData> Data { get; set; } = new();
}

public class OKXAccountData
{
    [JsonPropertyName("details")]
    public List<OKXBalanceDetail> Details { get; set; } = new();
}

public class OKXBalanceDetail
{
    [JsonPropertyName("ccy")]
    public string Ccy { get; set; } = string.Empty;

    [JsonPropertyName("availBal")]
    public string AvailBal { get; set; } = string.Empty;

    [JsonPropertyName("frozenBal")]
    public string FrozenBal { get; set; } = string.Empty;
}
