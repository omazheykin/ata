using System.Net.Http.Json;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class BinanceRealState : BinanceBaseState
{
    public BinanceRealState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret) 
        : base(httpClient, logger, apiKey, apiSecret, "https://api.binance.com")
    {
    }

    // Inherits GetOrderBookAsync from base

    public override async Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}";
            var signature = Sign(query);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v3/account?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (0.001m, 0.001m);

            var account = await response.Content.ReadFromJsonAsync<BinanceAccountResponse>();
            return account == null ? (0.001m, 0.001m) : (account.CommissionRates.Maker, account.CommissionRates.Taker);
        }
        catch
        {
            return (0.001m, 0.001m);
        }
    }

    public override async Task<List<Balance>> GetBalancesAsync()
    {
        try
        {
            Logger.LogInformation("Fetching balances from Binance");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}";
            var signature = Sign(query);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v3/account?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) {
                Logger.LogError("Failed to fetch balances from Binance: {StatusCode} / {StatusPhrase}", response.StatusCode, response.ReasonPhrase);
                return new List<Balance>();
            }

            var account = await response.Content.ReadFromJsonAsync<BinanceAccountResponse>();
            if (account?.Balances == null) return new List<Balance>();

            Logger.LogInformation("Balances fetched from Binance");
            return account.Balances
                .Where(b => decimal.Parse(b.Free, System.Globalization.CultureInfo.InvariantCulture) > 0 || 
                            decimal.Parse(b.Locked, System.Globalization.CultureInfo.InvariantCulture) > 0)
                .Select(b => new Balance
                {
                    Asset = b.Asset,
                    Free = decimal.Parse(b.Free, System.Globalization.CultureInfo.InvariantCulture),
                    Locked = decimal.Parse(b.Locked, System.Globalization.CultureInfo.InvariantCulture)
                }).ToList();
        }
        catch
        {
            return new List<Balance>();
        }
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        // No-op for real state
        return Task.CompletedTask;
    }
}
