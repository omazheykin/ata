using ArbitrageApi.Models;
using System.Collections.Concurrent;

namespace ArbitrageApi.Services.Exchanges.OKX;

public class OKXSandboxState : OKXBaseState
{
    private readonly ConcurrentDictionary<string, decimal> _balances = new();
    private readonly IOKXState _realState;

    public OKXSandboxState(
        HttpClient httpClient,
        ILogger<OKXSandboxState> logger,
        string apiKey,
        string secretKey,
        string passphrase,
        string baseUrl,
        IOKXState realState)
        : base(httpClient, logger, apiKey, secretKey, passphrase, baseUrl)
    {
        _realState = realState;
        
        // Initialize with default funds
        _balances["USDT"] = 100000m;
        _balances["BTC"] = 10m;
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

    public override Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        // Return mock OKX fees for sandbox testing
        return Task.FromResult<(decimal, decimal)?>((0.0008m, 0.001m));
    }

    public override Task<Dictionary<string, ExchangePrice>> GetPricesAsync(string[] symbols)
    {
        // Use REAL prices for simulation accuracy
        return _realState.GetPricesAsync(symbols);
    }

    public override Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        // Use REAL order book for simulation accuracy
        return _realState.GetOrderBookAsync(symbol, limit);
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
        Logger.LogInformation("🧪 [Sandbox] Mock OKX Withdrawal of {Amount} {Asset} to {Address}", amount, asset, address);
        return Task.FromResult($"mock_okx_tx_{Guid.NewGuid()}");
    }

    public override Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default)
    {
        var mockAddr = $"SANDBOX_OKX_{asset.ToUpper()}";
        Logger.LogInformation("🧪 [Sandbox] Mock OKX Deposit Address for {Asset}: {Address}", asset, mockAddr);
        return Task.FromResult<string?>(mockAddr);
    }

    public override Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        _balances.AddOrUpdate(asset, amount, (_, old) => old + amount);
        Logger.LogInformation("💰 SANDBOX DEPOSIT (OKX): {Amount} {Asset}", amount, asset);
        return Task.CompletedTask;
    }
}
