using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArbitrageApi.Services;

public interface ITrendAnalysisService
{
    Task<AssetTrend> GetTrendAsync(string asset, CancellationToken ct = default);
    Task<RebalanceWindow?> GetBestWindowAsync(CancellationToken ct = default);
}

public class AssetTrend
{
    public string Asset { get; set; } = string.Empty;
    public decimal Skew24h { get; set; } // -1.0 to 1.0 (activity skew)
    public int TradeCount24h { get; set; }
    public string Prediction { get; set; } = "Neutral";
}

public class RebalanceWindow
{
    public string Day { get; set; } = string.Empty;
    public string Hour { get; set; } = string.Empty;
    public decimal ActivityLevel { get; set; } // Lower is better
    public bool IsCurrent { get; set; }
}

public class TrendAnalysisService : ITrendAnalysisService
{
    private readonly ILogger<TrendAnalysisService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public TrendAnalysisService(ILogger<TrendAnalysisService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<AssetTrend> GetTrendAsync(string asset, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        var cutoff = DateTime.UtcNow.AddHours(-24);
        var transactions = await db.Transactions
            .Where(t => t.Asset == asset && t.Timestamp > cutoff && t.Status == "Success")
            .ToListAsync(ct);

        if (!transactions.Any())
        {
            return new AssetTrend { Asset = asset, Prediction = "Neutral (No Data)" };
        }

        // Calculate Skew based on BuyExchange vs SellExchange in Transaction string "A -> B"
        int toBinance = 0;
        int toCoinbase = 0;

        foreach (var t in transactions)
        {
            if (t.Exchange.Contains("→ Binance")) toBinance++;
            else if (t.Exchange.Contains("→ Coinbase")) toCoinbase++;
        }

        decimal skew = 0;
        if (toBinance + toCoinbase > 0)
        {
            skew = (decimal)(toBinance - toCoinbase) / (toBinance + toCoinbase);
        }

        string prediction = "Neutral";
        if (skew > 0.3m) prediction = "Binance-ward Trend";
        else if (skew < -0.3m) prediction = "Coinbase-ward Trend";

        return new AssetTrend
        {
            Asset = asset,
            Skew24h = Math.Round(skew, 2),
            TradeCount24h = transactions.Count,
            Prediction = prediction
        };
    }

    public async Task<RebalanceWindow?> GetBestWindowAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        // Find the hour with the lowest activity across all data
        var hourStats = await db.AggregatedMetrics
            .Where(m => m.Category == "Hour")
            .OrderBy(m => m.EventCount)
            .Take(1)
            .FirstOrDefaultAsync(ct);

        if (hourStats == null) return null;

        // MetricKey for Hour Category is "Mon-12"
        var parts = hourStats.MetricKey.Split('-');
        if (parts.Length < 2) return null;

        var day = parts[0];
        var hour = parts[1];

        var now = DateTime.UtcNow;
        var currentDay = now.DayOfWeek.ToString().Substring(0, 3);
        var currentHour = now.Hour.ToString("D2");

        return new RebalanceWindow
        {
            Day = day,
            Hour = hour,
            ActivityLevel = hourStats.EventCount,
            IsCurrent = day == currentDay && hour == currentHour
        };
    }
}
