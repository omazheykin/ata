namespace ArbitrageApi.Models;

/// <summary>
/// Type of order to place
/// </summary>
public enum OrderType
{
    Market,
    Limit
}

/// <summary>
/// Side of the order (buy or sell)
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// Status of an order
/// </summary>
public enum OrderStatus
{
    Pending,
    PartiallyFilled,
    Filled,
    Cancelled,
    Failed,
    Rejected
}

/// <summary>
/// Execution strategy for arbitrage trades
/// </summary>
public enum ExecutionStrategy
{
    Sequential, // Buy then Sell
    Concurrent  // Buy and Sell simultaneously
}

/// <summary>
/// Request to place an order
/// </summary>
public class OrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Price { get; set; } // Only for limit orders
    public string? ClientOrderId { get; set; }
}

/// <summary>
/// Response from exchange after placing an order
/// </summary>
public class OrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string? ClientOrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public OrderSide Side { get; set; }
    public OrderStatus Status { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal ExecutedQuantity { get; set; }
    public decimal? Price { get; set; }
    public decimal? AveragePrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Fee Info
    public decimal Fee { get; set; }
    public string FeeCurrency { get; set; } = string.Empty;
}

/// <summary>
/// Detailed order information
/// </summary>
public class OrderInfo
{
    public string OrderId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public OrderSide Side { get; set; }
    public OrderStatus Status { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal ExecutedQuantity { get; set; }
    public decimal RemainingQuantity => OriginalQuantity - ExecutedQuantity;
    public decimal? Price { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Fee { get; set; }
    public string FeeCurrency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime Timestamp { get; set; }
}
