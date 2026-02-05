using System.ComponentModel.DataAnnotations;

namespace ArbitrageApi.Models;

public class HeatmapCell
{
    [Key]
    public string Id { get; set; } = string.Empty; // e.g., "Mon-16"

    public string Day { get; set; } = string.Empty;
    public int Hour { get; set; }

    public int EventCount { get; set; }
    public decimal AvgSpread { get; set; }
    public decimal MaxSpread { get; set; }
    public string DirectionBias { get; set; } = string.Empty;
    public decimal VolatilityScore { get; set; }
}
