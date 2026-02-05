using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public interface IBookProvider
{
    string ExchangeName { get; }
    (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)? GetOrderBook(string symbol);
    Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();
    Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync();
    ConnectionStatus GetConnectionStatus();
}
