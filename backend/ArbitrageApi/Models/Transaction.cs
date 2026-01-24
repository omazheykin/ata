using System;

namespace ArbitrageApi.Models;

public class Transaction
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // "Buy", "Sell", "Arbitrage"
    public string Asset { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }
    public string Status { get; set; } = "Success";
    
    // Order tracking fields
    public string? BuyOrderId { get; set; }
    public string? SellOrderId { get; set; }
    public string? BuyOrderStatus { get; set; }
    public string? SellOrderStatus { get; set; }
    
    // Advanced strategy fields
    public ExecutionStrategy Strategy { get; set; }
    public bool IsRecovered { get; set; }
    public string? RecoveryOrderId { get; set; }
}
