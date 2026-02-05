namespace ArbitrageApi.Models;

public class ArbitrageOpportunity
{
    public Guid Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string BuyExchange { get; set; } = string.Empty;
    public string SellExchange { get; set; } = string.Empty;
    public decimal BuyPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal BuyFee { get; set; }
    public decimal SellFee { get; set; }
    public decimal ProfitPercentage { get; set; }
    public decimal GrossProfitPercentage { get; set; }
    public decimal Volume { get; set; }
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsSandbox { get; set; }
    public decimal BuyDepth { get; set; }
    public decimal SellDepth { get; set; }
}
