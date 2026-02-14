using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges.OKX;

public class OKXClient : IExchangeClient
{
    private IOKXState _currentState;
    private readonly IOKXState _realState;
    private readonly IOKXState _sandboxState;
    private readonly ILogger<OKXClient> _logger;
    private bool _isSandbox;

    public string ExchangeName => "OKX";

    public OKXClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        bool isSandboxMode = false)
    {
        _logger = loggerFactory.CreateLogger<OKXClient>();

        var apiKey = configuration["OKX:ApiKey"] ?? "";
        var secretKey = configuration["OKX:SecretKey"] ?? "";
        var passphrase = configuration["OKX:Passphrase"] ?? "";
        var baseUrl = configuration["OKX:BaseUrl"] ?? "https://www.okx.com";

        var httpClient = httpClientFactory.CreateClient("OKX");
        httpClient.BaseAddress = new Uri(baseUrl);

        _realState = new OKXRealState(
            httpClient,
            loggerFactory.CreateLogger<OKXRealState>(),
            apiKey,
            secretKey,
            passphrase,
            baseUrl);

        _sandboxState = new OKXSandboxState(
            httpClient,
            loggerFactory.CreateLogger<OKXSandboxState>(),
            apiKey,
            secretKey,
            passphrase,
            baseUrl,
            _realState);

        _isSandbox = isSandboxMode;
        _currentState = isSandboxMode ? _sandboxState : _realState;
    }

    public Task<ExchangePrice?> GetPriceAsync(string symbol)
    {
        // OKX implementation uses GetPricesAsync bulk fetch, 
        // but we can implement this by calling the bulk one for a single symbol
        return Task.FromResult<ExchangePrice?>(null); 
    }

    public async Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols)
    {
        return await _currentState.GetPricesAsync(symbols.ToArray());
    }

    public List<string> GetSupportedSymbols()
    {
        return TradingPair.CommonPairs.Select(p => p.Symbol).ToList();
    }

    public Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        return _currentState.GetOrderBookAsync(symbol, limit);
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync()
    {
        return _currentState.GetCachedFeesAsync();
    }

    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync()
    {
        return _currentState.GetCachedFeesAsync();
    }

    public Task<List<Balance>> GetBalancesAsync()
    {
        return _currentState.GetBalancesAsync();
    }

    public Task<List<Balance>> GetCachedBalancesAsync()
    {
        return _currentState.GetCachedBalancesAsync();
    }

    public Task<decimal?> GetWithdrawalFeeAsync(string asset)
    {
        return _currentState.GetWithdrawalFeeAsync(asset);
    }

    public Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null)
    {
        return _currentState.WithdrawAsync(asset, amount, address, network);
    }

    public Task<string?> GetDepositAddressAsync(string asset, CancellationToken ct = default)
    {
        return _currentState.GetDepositAddressAsync(asset, ct);
    }

    public void SetSandboxMode(bool enabled)
    {
        _isSandbox = enabled;
        _currentState = enabled ? _sandboxState : _realState;
        _logger.LogInformation("OKX mode switched to {Mode}", enabled ? "Sandbox" : "Real");
    }

    // Order placement methods
    public Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
    {
        return _currentState.PlaceMarketBuyOrderAsync(symbol, quantity);
    }

    public Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
    {
        return _currentState.PlaceMarketSellOrderAsync(symbol, quantity);
    }

    public Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return _currentState.PlaceLimitBuyOrderAsync(symbol, quantity, price);
    }

    public Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
    {
        return _currentState.PlaceLimitSellOrderAsync(symbol, quantity, price);
    }

    public Task<OrderInfo> GetOrderStatusAsync(string orderId)
    {
        return _currentState.GetOrderStatusAsync(orderId);
    }

    public Task<bool> CancelOrderAsync(string orderId)
    {
        return _currentState.CancelOrderAsync(orderId);
    }

    public Task DepositSandboxFundsAsync(string asset, decimal amount)
    {
        return _currentState.DepositSandboxFundsAsync(asset, amount);
    }
}
