using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public interface IExchangeState
{
    Task<ExchangePrice?> GetPriceAsync(string symbol);
    Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols);
    Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();
    Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync();
    Task<List<Balance>> GetBalancesAsync();
    Task<decimal?> GetWithdrawalFeeAsync(string asset);
    Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null);
    Task UpdateSymbolMappingWithSupportedProductsAsync();
    
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
