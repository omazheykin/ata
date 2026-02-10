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

    public override Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null)
    {
        // POST /sapi/v1/capital/withdraw/apply (Mock for now)
        throw new NotImplementedException("Binance real withdrawals not yet enabled for safety.");
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
            if (account?.Balances == null) 
            {
                Logger.LogWarning("Binance balances response or balances list is null, using cache.");
                return CachedBalances;
            }

            Logger.LogInformation("Balances fetched from Binance");
            var freshBalances = account.Balances
                .Where(b => decimal.Parse(b.Free, System.Globalization.CultureInfo.InvariantCulture) > 0 || 
                            decimal.Parse(b.Locked, System.Globalization.CultureInfo.InvariantCulture) > 0)
                .Select(b => new Balance
                {
                    Asset = b.Asset,
                    Free = decimal.Parse(b.Free, System.Globalization.CultureInfo.InvariantCulture),
                    Locked = decimal.Parse(b.Locked, System.Globalization.CultureInfo.InvariantCulture)
                }).ToList();

            if (freshBalances.Any())
            {
                CachedBalances = freshBalances;
                LastBalanceUpdate = DateTime.UtcNow;
            }
            
            return freshBalances;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching balances from Binance, returning cache.");
            return CachedBalances;
        }
    }

    public override async System.Threading.Tasks.Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"coin={asset.ToUpper()}&timestamp={timestamp}";
            var signature = Sign(query);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/sapi/v1/capital/deposit/address?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", ApiKey);

            var response = await HttpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                Logger.LogError("Failed to fetch deposit address from Binance for {Asset}: {StatusCode} - {Error}", asset, response.StatusCode, error);
                return null;
            }

            var addressInfo = await response.Content.ReadFromJsonAsync<BinanceDepositAddressResponse>(ct);
            return addressInfo?.Address;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching deposit address from Binance for {Asset}", asset);
            return null;
        }
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        // No-op for real state
        return Task.CompletedTask;
    }
}
