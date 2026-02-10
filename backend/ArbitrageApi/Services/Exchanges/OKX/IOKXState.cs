namespace ArbitrageApi.Services.Exchanges.OKX;

public interface IOKXState
{
    string ExchangeName { get; }
    Task<Dictionary<string, Models.ExchangePrice>> GetPricesAsync(string[] symbols);
    Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20);
    Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync();
    Task<List<Models.Balance>> GetBalancesAsync();
    Task<List<Models.Balance>> GetCachedBalancesAsync();
    Task<decimal?> GetWithdrawalFeeAsync(string asset);
    Task<string> WithdrawAsync(string asset, decimal amount, string address, string? network = null);
    Task<string?> GetDepositAddressAsync(string asset, System.Threading.CancellationToken ct = default);
    Task DepositSandboxFundsAsync(string asset, decimal amount);
    
    // Order methods
    Task<Models.OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity);
    Task<Models.OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity);
    Task<Models.OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price);
    Task<Models.OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price);
    Task<Models.OrderInfo> GetOrderStatusAsync(string orderId);
    Task<bool> CancelOrderAsync(string orderId);
}
