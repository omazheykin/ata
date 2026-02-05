using ArbitrageApi.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ArbitrageApi.Services.Exchanges.OKX;

public abstract class OKXBaseState : IOKXState
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;
    protected readonly string ApiKey;
    protected readonly string SecretKey;
    protected readonly string Passphrase;
    protected readonly string BaseUrl;

    public string ExchangeName => "OKX";

    private (decimal Maker, decimal Taker)? _cachedFees;
    private DateTime _lastFeeUpdate = DateTime.MinValue;
    private readonly TimeSpan _feeTtl = TimeSpan.FromHours(1);
    protected List<Balance> CachedBalances = new();

    protected OKXBaseState(
        HttpClient httpClient,
        ILogger logger,
        string apiKey,
        string secretKey,
        string passphrase,
        string baseUrl)
    {
        HttpClient = httpClient;
        Logger = logger;
        ApiKey = apiKey;
        SecretKey = secretKey;
        Passphrase = passphrase;
        BaseUrl = baseUrl;
    }

    protected string Sign(string timestamp, string method, string path, string body)
    {
        // OKX signature: Base64(HMAC-SHA256(timestamp + method + path + body, secret))
        var message = timestamp + method.ToUpper() + path + body;
        var keyBytes = Encoding.UTF8.GetBytes(SecretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hashBytes);
    }

    protected void AddOKXHeaders(HttpRequestMessage request, string timestamp, string method, string path, string body)
    {
        var signature = Sign(timestamp, method, path, body);
        request.Headers.Add("OK-ACCESS-KEY", ApiKey);
        request.Headers.Add("OK-ACCESS-SIGN", signature);
        request.Headers.Add("OK-ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("OK-ACCESS-PASSPHRASE", Passphrase);
    }

    public virtual async Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync()
    {
        if (_cachedFees != null && (DateTime.UtcNow - _lastFeeUpdate) < _feeTtl)
        {
            Logger.LogDebug("💰 [OKX] Using cached fees: Maker={Maker}, Taker={Taker}", _cachedFees.Value.Maker, _cachedFees.Value.Taker);
            return _cachedFees;
        }

        Logger.LogInformation("🔄 [OKX] Fetching fresh fees from API...");
        var fees = await GetSpotFeesAsync();
        if (fees != null)
        {
            _cachedFees = fees;
            _lastFeeUpdate = DateTime.UtcNow;
            Logger.LogInformation("✅ [OKX] Fees retrieved: Maker={Maker}, Taker={Taker}", fees.Value.Maker, fees.Value.Taker);
        }
        else
        {
            Logger.LogWarning("⚠️ [OKX] Failed to retrieve fees, using default: 0.0008 (0.08%)");
        }
        return _cachedFees ?? (0.0008m, 0.001m);
    }

    public abstract Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();
    public abstract Task<Dictionary<string, ExchangePrice>> GetPricesAsync(string[] symbols);
    public abstract Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20);
    public abstract Task<List<Balance>> GetBalancesAsync();
    
    public virtual Task<decimal?> GetWithdrawalFeeAsync(string asset)
    {
        var fee = asset.ToUpper() switch
        {
            "BTC" => 0.0004m,
            "ETH" => 0.003m,
            "USDT" => 4.0m,
            "USDC" => 4.0m,
            "SOL" => 0.01m,
            "XRP" => 0.25m,
            _ => 1.0m
        };
        return Task.FromResult<decimal?>(fee);
    }

    public abstract Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null);
    
    public abstract Task DepositSandboxFundsAsync(string asset, decimal amount);
}
