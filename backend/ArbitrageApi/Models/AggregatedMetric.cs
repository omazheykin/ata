using System.ComponentModel.DataAnnotations;

namespace ArbitrageApi.Models;

public class AggregatedMetric
{
    [Key]
    public string Id { get; set; } = string.Empty; // e.g., "Pair:BTCUSDT" or "Hour:Mon-12"

    public string Category { get; set; } = string.Empty; // Pair, Hour, Day, Global
    
    public string MetricKey { get; set; } = string.Empty; // Symbol, HourId, etc.

    public int EventCount { get; set; }
    
    public decimal SumSpread { get; set; }
    
    public decimal MaxSpread { get; set; }
    
    public decimal SumDepth { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
