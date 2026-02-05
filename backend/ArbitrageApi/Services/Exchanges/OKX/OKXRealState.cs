using ArbitrageApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

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
