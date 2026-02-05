namespace ArbitrageApi.Services.Exchanges.OKX;

public interface IOKXState
{
    string ExchangeName { get; }
    Task<Dictionary<string, Models.ExchangePrice>> GetPricesAsync(string[] symbols);
    Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20);
    Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync();
    Task<List<Models.Balance>> GetBalancesAsync();
    Task DepositSandboxFundsAsync(string asset, decimal amount);
}
