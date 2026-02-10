using System.Net.Http.Json;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges.Coinbase;
namespace ArbitrageApi.Services.Exchanges;

public class CoinbaseRealState : CoinbaseBaseState
{
    public CoinbaseRealState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret) 
        : base(httpClient, logger, apiKey, apiSecret, "https://api.coinbase.com")
    {
    }

    public override async Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        try
        {
            // SymbolMapping should be updated once at startup, not per request
            if (!SymbolMapping.TryGetValue(symbol, out var coinbaseSymbol)) return null;

            var response = await HttpClient.GetFromJsonAsync<CoinbasePriceResponse>(
                $"{BaseUrl}/v2/prices/{coinbaseSymbol}/spot");

            if (response?.Data == null) return null;

            return new ExchangePrice
            {
                Exchange = ExchangeName,
                Symbol = symbol,
                Price = decimal.Parse(response.Data.Amount, System.Globalization.CultureInfo.InvariantCulture),
                Timestamp = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    public override async Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        try
        {
            var requestPath = "/api/v3/brokerage/transaction_summary";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = Sign(timestamp, "GET", requestPath, "", false);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{requestPath}");
            request.Headers.Add("CB-ACCESS-KEY", ApiKey);
            request.Headers.Add("CB-ACCESS-SIGN", signature);
            request.Headers.Add("CB-ACCESS-TIMESTAMP", timestamp);

            var response = await HttpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Coinbase fee API returned {StatusCode}", response.StatusCode);
                return (0.001m, 0.001m);
            }
            
            var summary = await response.Content.ReadFromJsonAsync<CoinbaseFeeResponse>();
            if (summary?.FeeTier != null)
            {
                return (summary.FeeTier.MakerFeeRate, summary.FeeTier.TakerFeeRate);
            }
            
            return (0.001m, 0.001m);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching Coinbase fees");
            return (0.001m, 0.001m);
        }
    }

    public override Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null)
    {
        // POST /v2/accounts/:account_id/withdrawals/crypto (Mock for now)
        throw new NotImplementedException("Coinbase real withdrawals not yet enabled for safety.");
    }

    public override async Task<List<Balance>> GetBalancesAsync()
    {
        try
        {
            Logger.LogInformation("Fetching balances from Coinbase");

            var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
            var accountResponse = await cats.GetAccountsAsync();

            if (accountResponse?.Accounts == null)
            {
                Logger.LogWarning("Coinbase accounts response or accounts list is null, using cache.");
                return CachedBalances;
            }

            var freshBalances = accountResponse.Accounts
                .Where(a => a.AvailableBalance?.Value != null || a.Hold?.Value != null)
                .Where(a => {
                    var available = decimal.TryParse(a.AvailableBalance?.Value ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var av) ? av : 0;
                    var hold = decimal.TryParse(a.Hold?.Value ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0;
                    return available > 0 || hold > 0;
                })
                .Select(a => new Balance
                {
                    Asset = a.Currency ?? "UNKNOWN",
                    Free = decimal.TryParse(a.AvailableBalance?.Value ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var av) ? av : 0,
                    Locked = decimal.TryParse(a.Hold?.Value ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0
                }).ToList();

            if (freshBalances.Any())
            {
                CachedBalances = freshBalances;
                LastBalanceUpdate = DateTime.UtcNow;
            }

            Logger.LogInformation("Fetched {Count} balances from Coinbase", freshBalances.Count);
            return freshBalances;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching balances from Coinbase, returning cache.");
            return CachedBalances;
        }
    }

    public override async Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Buy, OrderType.Market, quantity, null);
    }

    public override async Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Sell, OrderType.Market, quantity, null);
    }

    public override async Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Buy, OrderType.Limit, quantity, price);
    }

    public override async Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return await PlaceOrderAsync(symbol, OrderSide.Sell, OrderType.Limit, quantity, price);
    }

    private async Task<OrderResponse> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price)
    {
        try
        {
            if (!SymbolMapping.TryGetValue(symbol, out var coinbaseSymbol))
            {
                return new OrderResponse { Status = OrderStatus.Failed, ErrorMessage = $"Symbol mapping not found for {symbol}" };
            }

            var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
            var request = new CoinbaseOrderRequest
            {
                ProductId = coinbaseSymbol,
                Side = side == OrderSide.Buy ? "BUY" : "SELL",
                OrderConfiguration = new OrderConfiguration()
            };

            if (type == OrderType.Market)
            {
                request.OrderConfiguration.MarketIoc = new MarketIoc { BaseSize = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }
            else
            {
                request.OrderConfiguration.LimitGtc = new LimitGtc 
                { 
                    BaseSize = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LimitPrice = price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"
                };
            }

            var response = await cats.CreateOrderAsync(request);

            if (response == null || !response.Success)
            {
                return new OrderResponse 
                { 
                    Status = OrderStatus.Failed, 
                    Symbol = symbol,
                    Side = side,
                    Type = type,
                    ErrorMessage = response?.ErrorMessage ?? "Empty response from Coinbase" 
                };
            }

            return new OrderResponse
            {
                OrderId = response.OrderId,
                Symbol = symbol,
                Side = side,
                Type = type,
                Status = OrderStatus.Pending, // Initial status
                OriginalQuantity = quantity,
                Price = price,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error placing order on Coinbase");
            return new OrderResponse { Status = OrderStatus.Failed, ErrorMessage = ex.Message };
        }
    }

    public override async Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
        var response = await cats.GetOrderAsync(orderId);

        if (response?.Order == null)
        {
            throw new Exception($"Failed to fetch order status for {orderId}");
        }

        return new OrderInfo
        {
            OrderId = response.Order.OrderId,
            Symbol = response.Order.ProductId ?? string.Empty,
            Status = MapCoinbaseStatus(response.Order.Status),
            OriginalQuantity = decimal.TryParse(response.Order.FilledSize, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0,
            ExecutedQuantity = decimal.TryParse(response.Order.FilledSize, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var es) ? es : 0,
            CreatedAt = DateTime.UtcNow // Historical API might not return this purely
        };
    }

    public override async Task<bool> CancelOrderAsync(string orderId)
    {
        var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
        return await cats.CancelOrdersAsync(new List<string> { orderId });
    }

    private OrderStatus MapCoinbaseStatus(string? status)
    {
        return status?.ToUpper() switch
        {
            "OPEN" => OrderStatus.Pending,
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "EXPIRED" => OrderStatus.Cancelled,
            "FAILED" => OrderStatus.Failed,
            _ => OrderStatus.Failed
        };
    }

    public override async System.Threading.Tasks.Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("Resolving deposit address for Coinbase {Asset}", asset);
            var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
            
            // 1. Get all accounts to find the UUID for this asset
            var accountResponse = await cats.GetAccountsAsync();
            var account = accountResponse?.Accounts?.FirstOrDefault(a => 
                string.Equals(a.Currency, asset, StringComparison.OrdinalIgnoreCase));

            if (account == null || string.IsNullOrEmpty(account.Uuid))
            {
                Logger.LogWarning("Could not find Coinbase account UUID for {Asset}", asset);
                return null;
            }

            // 2. Fetch/Create deposit address for this account
            return await cats.GetDepositAddressAsync(account.Uuid);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching deposit address from Coinbase for {Asset}", asset);
            return null;
        }
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        // No-op for real state
        return Task.CompletedTask;
    }
}
