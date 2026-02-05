using System.ComponentModel.DataAnnotations;

namespace ArbitrageApi.Models;

public class ArbitrageEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Pair { get; set; } = string.Empty;

    [Required]
    public string Direction { get; set; } = string.Empty;

    [Required]
    public decimal Spread { get; set; }

    [Required]
    public decimal DepthBuy { get; set; }

    [Required]
    public decimal DepthSell { get; set; }

    [Required]
    public DateTime Timestamp { get; set; }

    [Required]
    public decimal SpreadPercent { get; set; }
}
