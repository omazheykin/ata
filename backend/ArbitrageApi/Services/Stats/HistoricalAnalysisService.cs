using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats;

/// <summary>
/// dedicated service for Historical Analysis (Phase 5).
/// Calculates Volatility, Duration, and Profitability Analytics.
/// </summary>
public class HistoricalAnalysisService
{
    private readonly StatsDbContext _context;
    private readonly ILogger<HistoricalAnalysisService> _logger;

    public HistoricalAnalysisService(StatsDbContext context, ILogger<HistoricalAnalysisService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Calculates the standard deviation of spreads for a given pair over a time window.
    /// </summary>
    public async Task<double> CalculateVolatilityIndexAsync(string pair, DateTime start, DateTime end)
    {
        var spreads = await _context.ArbitrageEvents
            .Where(e => e.Pair == pair && e.Timestamp >= start && e.Timestamp <= end)
            .Select(e => (double)e.SpreadPercent)
            .ToListAsync();

        if (spreads.Count < 2) return 0;

        var avg = spreads.Average();
        var sumSquares = spreads.Sum(d => Math.Pow(d - avg, 2));
        return Math.Sqrt(sumSquares / (spreads.Count - 1));
    }

    /// <summary>
    /// Returns the most profitable pairs based on Realized Profit (from Transactions).
    /// </summary>
    public async Task<List<PairProfitabilityDto>> GetTopProfitablePairsAsync(int topN = 10)
    {
        // Group transactions by Asset (approx Pair).
        // Since Transaction has 'Asset' (e.g. BTC) and 'Type' (Arbitrage), we sum RealizedProfit.
        // Ideally we should track 'Pair' in Transaction or infer it.
        // Assuming Transaction.Asset is the Base Asset (e.g. BTC).
        
        var stats = await _context.Transactions
            .Where(t => t.Type == "Arbitrage" && t.Status == "Success")
            .GroupBy(t => t.Pair)
            .Select(g => new PairProfitabilityDto
            {
                Pair = g.Key,
                TotalProfit = g.Sum(t => t.RealizedProfit),
                TradeCount = g.Count()
            })
            .OrderByDescending(p => p.TotalProfit)
            .Take(topN)
            .ToListAsync();

        return stats;
    }

    public async Task<List<RouteProfitabilityDto>> GetTopRoutesAsync(int topN = 10)
    {
        var stats = await _context.Transactions
            .Where(t => t.Type == "Arbitrage" && t.Status == "Success" && t.BuyExchange != null && t.SellExchange != null)
            .GroupBy(t => new { t.BuyExchange, t.SellExchange })
            .Select(g => new RouteProfitabilityDto
            {
                Route = $"{g.Key.BuyExchange} -> {g.Key.SellExchange}",
                TotalProfit = g.Sum(t => t.RealizedProfit),
                TradeCount = g.Count()
            })
            .OrderByDescending(r => r.TotalProfit)
            .Take(topN)
            .ToListAsync();

        return stats;
    }
}

public class PairProfitabilityDto
{
    public string Pair { get; set; }
    public decimal TotalProfit { get; set; }
    public int TradeCount { get; set; }
}

public class RouteProfitabilityDto
{
    public string Route { get; set; } // "Binance->Coinbase"
    public decimal TotalProfit { get; set; }
    public int TradeCount { get; set; }
}
