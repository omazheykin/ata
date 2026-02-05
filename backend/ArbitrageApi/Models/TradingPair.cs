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
        new TradingPair("POL", "USDT"),
        new TradingPair("LINK", "USDT")
    };

    public string GetCoinbaseSymbol()
    {
        // Coinbase uses BASE-USD format for most pairs
        // Handle POL rebranding if needed, though POL-USD is standard on Coinbase now
        return $"{BaseAsset}-USD";
    }

    public string GetBinanceSymbol()
    {
        return $"{BaseAsset}{QuoteAsset}";
    }

    public string GetOKXSymbol()
    {
        // OKX uses BASE-QUOTE format with hyphen (e.g., BTC-USDT)
        return $"{BaseAsset}-{QuoteAsset}";
    }
}
