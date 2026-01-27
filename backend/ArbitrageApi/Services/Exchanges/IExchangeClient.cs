using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public interface IExchangeClient
{
    string ExchangeName { get; }
    Task<ExchangePrice?> GetPriceAsync(string symbol);
    Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols);
    List<string> GetSupportedSymbols();
    Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();
    Task<List<Balance>> GetBalancesAsync();
    void SetSandboxMode(bool enabled);

    /// <summary>
    /// Fetches the order book (depth) for a symbol. Returns a tuple of bids and asks, each as a list of (price, quantity).
    /// </summary>
    Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20);
    
    // Order placement methods
    Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity);
    Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity);
    Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price);
    Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price);
    
    // Order management
    Task<OrderInfo> GetOrderStatusAsync(string orderId);
    Task<bool> CancelOrderAsync(string orderId);

    // Sandbox management
    Task DepositSandboxFundsAsync(string asset, decimal amount);
}
