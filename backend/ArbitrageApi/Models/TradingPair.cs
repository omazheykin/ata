namespace ArbitrageApi.Models;

public class TradingPair
{
    public string Symbol { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;

    public TradingPair(string baseAsset, string quoteAsset)
    {
        BaseAsset = baseAsset;
        QuoteAsset = quoteAsset;
        Symbol = $"{baseAsset}{quoteAsset}";
    }

    public static readonly List<TradingPair> CommonPairs = new()
    {
        new TradingPair("BTC", "USDT"),
        new TradingPair("ETH", "USDT"),
        new TradingPair("BNB", "USDT"),
        new TradingPair("SOL", "USDT"),
        new TradingPair("XRP", "USDT"),
        new TradingPair("ADA", "USDT"),
        new TradingPair("AVAX", "USDT"),
        new TradingPair("DOT", "USDT"),
        new TradingPair("MATIC", "USDT"),
        new TradingPair("LINK", "USDT")
    };
}
