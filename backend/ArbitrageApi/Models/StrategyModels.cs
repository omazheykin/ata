namespace ArbitrageApi.Models;

public class StrategyUpdate
{
    public decimal MinProfitThreshold { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public decimal VolatilityScore { get; set; }
    public decimal CountScore { get; set; }
    public decimal SpreadScore { get; set; }
}
