namespace ArbitrageApi.Configuration;

public class PairsConfigRoot
{
    public List<PairConfig> Pairs { get; set; } = new();

    // Fast lookup dictionary
    private Dictionary<string, PairConfig>? _pairsDict;
    public PairConfig? this[string symbol]
    {
        get
        {
            if (_pairsDict == null)
            {
                _pairsDict = Pairs.ToDictionary(p => p.Symbol, p => p, StringComparer.OrdinalIgnoreCase);
            }
            return _pairsDict.TryGetValue(symbol, out var config) ? config : null;
        }
    }
}

public class PairConfig
{
    public string Symbol { get; set; } = string.Empty;
    
    // For specific exchange pairs (optional, for future use)
    public string? BinanceSymbol { get; set; }
    public string? CoinbaseSymbol { get; set; }

    // Depth Thresholds
    public double MinDepth { get; set; } = 0.5;
    public double OptimalDepth { get; set; } = 1.0;
    public double AggressiveDepth { get; set; } = 0.1;

    // Technical Filter (Dust)
    public double TechnicalMinDepth { get; set; } = 0.01;
}
