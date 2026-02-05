using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class BinanceClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BinanceClient> _logger;
    private IExchangeState _currentState;
    private readonly IExchangeState _realState;
    private readonly IExchangeState _sandboxState;
    private bool _isSandbox;

    public string ExchangeName => "Binance";

    public BinanceClient(HttpClient httpClient, ILogger<BinanceClient> logger, IConfiguration configuration, bool isSandboxMode = false)
    {
        _httpClient = httpClient;
        _logger = logger;
        _isSandbox = isSandboxMode;
        
        var realApiKey = configuration["Binance:ApiKey"] ?? string.Empty;
        var realApiSecret = configuration["Binance:ApiSecret"] ?? string.Empty;
        var sandboxApiKey = configuration["Binance:SandboxApiKey"] ?? string.Empty;
        var sandboxApiSecret = configuration["Binance:SandboxApiSecret"] ?? string.Empty;

        _realState = new BinanceRealState(httpClient, logger, realApiKey, realApiSecret);
        _sandboxState = new BinanceSandboxState(httpClient, logger, sandboxApiKey, sandboxApiSecret, _realState);
        
        _currentState = _isSandbox ? _sandboxState : _realState;
    }

    public void SetSandboxMode(bool enabled)
    {
        _isSandbox = enabled;
        _currentState = enabled ? _sandboxState : _realState;
        _logger.LogInformation("Binance mode switched to {Mode}", enabled ? "Sandbox" : "Real");
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync() => _currentState.GetSpotFeesAsync();
    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync() => _currentState.GetCachedFeesAsync();
    public Task<ExchangePrice?> GetPriceAsync(string symbol) => _currentState.GetPriceAsync(symbol);
    public Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols) => _currentState.GetPricesAsync(symbols);
    public Task<List<Balance>> GetBalancesAsync() => _currentState.GetBalancesAsync();
    public Task<decimal?> GetWithdrawalFeeAsync(string asset) => _currentState.GetWithdrawalFeeAsync(asset);
    public Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null) => _currentState.WithdrawAsync(asset, amount, address, network);

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
    
    // Order management
    public Task<OrderInfo> GetOrderStatusAsync(string orderId)
        => _currentState.GetOrderStatusAsync(orderId);
    
    public Task<bool> CancelOrderAsync(string orderId)
        => _currentState.CancelOrderAsync(orderId);

    // Sandbox management
    public Task DepositSandboxFundsAsync(string asset, decimal amount)
        => _currentState.DepositSandboxFundsAsync(asset, amount);
}
