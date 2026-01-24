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
            if (!response.IsSuccessStatusCode) return (0.005m, 0.005m);

            var summary = await response.Content.ReadFromJsonAsync<CoinbaseFeeResponse>();
            return summary?.FeeTier == null ? (0.005m, 0.005m) : (summary.FeeTier.MakerFeeRate, summary.FeeTier.TakerFeeRate);
        }
        catch
        {
            return (0.005m, 0.005m);
        }
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
                Logger.LogWarning("Coinbase accounts response or accounts list is null");
                return new List<Balance>();
            }

            var result = accountResponse.Accounts
                .Where(a => a.AvailableBalance != null && a.Hold != null)
                .Where(a => {
                    var available = decimal.TryParse(a.AvailableBalance.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var av) ? av : 0;
                    var hold = decimal.TryParse(a.Hold.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0;
                    return available > 0 || hold > 0;
                })
                .Select(a => new Balance
                {
                    Asset = a.Currency ?? "UNKNOWN",
                    Free = decimal.TryParse(a.AvailableBalance.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var av) ? av : 0,
                    Locked = decimal.TryParse(a.Hold.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0
                }).ToList();

            Logger.LogInformation("Fetched {Count} balances from Coinbase", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching balances from Coinbase");
            return new List<Balance>();
        }
    }

    public override Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        throw new NotImplementedException("Real order placement for Coinbase is not yet implemented.");
    }

    public override Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        throw new NotImplementedException("Real order placement for Coinbase is not yet implemented.");
    }

    public override Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        throw new NotImplementedException("Real order placement for Coinbase is not yet implemented.");
    }

    public override Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        throw new NotImplementedException("Real order placement for Coinbase is not yet implemented.");
    }

    public override Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        throw new NotImplementedException("Real order status check for Coinbase is not yet implemented.");
    }

    public override Task<bool> CancelOrderAsync(string orderId)
    {
        throw new NotImplementedException("Real order cancellation for Coinbase is not yet implemented.");
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        // No-op for real state
        return Task.CompletedTask;
    }
}
