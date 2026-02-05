namespace ArbitrageApi.Models;

public class StatsSummary
{
    public Dictionary<string, PairStats> Pairs { get; set; } = new();
    public Dictionary<int, HourStats> Hours { get; set; } = new();
    public Dictionary<string, DayStats> Days { get; set; } = new();
    public decimal GlobalVolatilityScore { get; set; }
    public Dictionary<string, int> DirectionDistribution { get; set; } = new();
    public double AvgSeriesDuration { get; set; }
    
    // PnL Stats
    public decimal TotalRealizedProfit { get; set; }
    public double SuccessRate { get; set; }
}

public class PairStats
{
    public int Count { get; set; }
    public decimal AvgSpread { get; set; }
    public decimal MaxSpread { get; set; }
}

public class HourStats
{
    public int Count { get; set; }
    public decimal AvgSpread { get; set; }
    public decimal MaxSpread { get; set; }
    public decimal AvgDepth { get; set; }
}

public class DayStats
{
    public int Count { get; set; }
    public decimal AvgSpread { get; set; }
}

public class HourDetail
{
    public double AvgOpportunitiesPerHour { get; set; }
    public decimal AvgSpread { get; set; }
    public decimal MaxSpread { get; set; }
    public int Count { get; set; }
    public decimal AvgDepth { get; set; }
    public string DirectionBias { get; set; } = string.Empty;
    public decimal VolatilityScore { get; set; }
    public string Zone { get; set; } = string.Empty;
}

public class RebalancingInfo
{
    public Dictionary<string, decimal> AssetSkews { get; set; } = new(); // Legacy support
    public Dictionary<string, Dictionary<string, decimal>> AssetDeviations { get; set; } = new(); // N-Exchange Support
    public string Recommendation { get; set; } = string.Empty;
    public decimal EfficiencyScore { get; set; }
    
    // New detailed breakdown
    public List<RebalancingProposal> Proposals { get; set; } = new();
}

public class RebalancingProposal
{
    public string Asset { get; set; } = string.Empty;
    public decimal Skew { get; set; }
    public string Direction { get; set; } = string.Empty; // "Binance -> Coinbase"
    public decimal Amount { get; set; }
    public decimal EstimatedFee { get; set; }
    public decimal CostPercentage { get; set; } // Fee / Amount
    public bool IsViable { get; set; } // True if cost < 1% (configurable)
    public string TrendDescription { get; set; } = string.Empty; // "Binance-ward Trend (24h)"
}

public class StatsResponse
{
    public StatsSummary Summary { get; set; } = new();
    public Dictionary<string, Dictionary<string, HourDetail>> Calendar { get; set; } = new();
    public RebalancingInfo Rebalancing { get; set; } = new();
}
