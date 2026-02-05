using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class CoinbaseClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CoinbaseClient> _logger;
    private IExchangeState _currentState;
    private readonly IExchangeState _realState;
    private readonly IExchangeState _sandboxState;
    private bool _isSandbox;

    public string ExchangeName => "Coinbase";

    public CoinbaseClient(HttpClient httpClient, ILogger<CoinbaseClient> logger, IConfiguration configuration, bool isSandboxMode = false)
    {
        _httpClient = httpClient;
        _logger = logger;
        _isSandbox = isSandboxMode;

        var realApiKey = configuration["Coinbase:ApiKey"] ?? string.Empty;
        var realApiSecret = configuration["Coinbase:ApiSecret"] ?? string.Empty;
        var sandboxApiKey = configuration["Coinbase:SandboxApiKey"] ?? string.Empty;
        var sandboxApiSecret = configuration["Coinbase:SandboxApiSecret"] ?? string.Empty;

        _realState = new CoinbaseRealState(httpClient, logger, realApiKey, realApiSecret);
        _sandboxState = new CoinbaseSandboxState(httpClient, logger, sandboxApiKey, sandboxApiSecret, _realState);

        _currentState = _isSandbox ? _sandboxState : _realState;

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ArbitrageApi");
        _logger.LogInformation("CoinbaseClient created. HashCode: {HashCode}", GetHashCode());
    }

    // Call this method after constructing CoinbaseClient and before using it for price/order book requests
    public async Task InitializeAsync()
    {
        await _currentState.UpdateSymbolMappingWithSupportedProductsAsync();
    }

    public void SetSandboxMode(bool enabled)
    {
        _isSandbox = enabled;
        _currentState = enabled ? _sandboxState : _realState;
        _logger.LogInformation("Coinbase mode switched to {Mode}", enabled ? "Sandbox" : "Real");
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync() => _currentState.GetSpotFeesAsync();
    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync() => _currentState.GetCachedFeesAsync();
    public Task<ExchangePrice?> GetPriceAsync(string symbol) => _currentState.GetPriceAsync(symbol);
    public Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols) => _currentState.GetPricesAsync(symbols);
    public Task<List<Balance>> GetBalancesAsync() => _currentState.GetBalancesAsync();
    public Task<decimal?> GetWithdrawalFeeAsync(string asset) => _currentState.GetWithdrawalFeeAsync(asset);
    public Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null) => _currentState.WithdrawAsync(asset, amount, address, network);
    public System.Threading.Tasks.Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default) => _currentState.GetDepositAddressAsync(asset, ct);

    public List<string> GetSupportedSymbols()
    {
        return TradingPair.CommonPairs.Select(p => p.Symbol).ToList();
    }

    public Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
        => _currentState.GetOrderBookAsync(symbol, limit);

    // Order placement methods
    public Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity)
        => _currentState.PlaceMarketBuyOrderAsync(symbol, quantity);

    public Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity)
        => _currentState.PlaceMarketSellOrderAsync(symbol, quantity);

    public Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price)
        => _currentState.PlaceLimitBuyOrderAsync(symbol, quantity, price);

    public Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price)
        => _currentState.PlaceLimitSellOrderAsync(symbol, quantity, price);

    // Order management methods
    public Task<OrderInfo> GetOrderStatusAsync(string orderId)
        => _currentState.GetOrderStatusAsync(orderId);

    public Task<bool> CancelOrderAsync(string orderId)
        => _currentState.CancelOrderAsync(orderId);

    // Sandbox management
    public Task DepositSandboxFundsAsync(string asset, decimal amount)
        => _currentState.DepositSandboxFundsAsync(asset, amount);
}
