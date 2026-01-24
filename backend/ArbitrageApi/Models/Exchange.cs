namespace ArbitrageApi.Models;

public static class Exchange
{
    public const string Binance = "Binance";
    public const string Coinbase = "Coinbase";
    public const string Kraken = "Kraken";
    public const string Bitfinex = "Bitfinex";
    public const string Huobi = "Huobi";
    public const string KuCoin = "KuCoin";

    public static readonly List<string> All = new()
    {
        Binance,
        Coinbase,
        Kraken,
        Bitfinex,
        Huobi,
        KuCoin
    };

    public static decimal GetFeePercentage(string exchange)
    {

        return exchange switch
        {
            Binance => 0.1m,
            Coinbase => 0.5m,
            Kraken => 0.26m,
            Bitfinex => 0.2m,
            Huobi => 0.2m,
            KuCoin => 0.1m,
            _ => 0.25m
        };
    }
}
