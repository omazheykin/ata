using System.Globalization;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats;

public interface IStatsAggregator
{
    Task UpdateMetricsAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext);
}

public class StatsAggregator : IStatsAggregator
{
    public async Task UpdateMetricsAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        var timestamp = arbitrageEvent.Timestamp;
        var dayLong = timestamp.DayOfWeek.ToString();
        var dayShort = timestamp.ToString("ddd", CultureInfo.InvariantCulture);
        var hour = timestamp.Hour;
        var spreadPercent = arbitrageEvent.Spread * 100;
        var avgDepth = (arbitrageEvent.DepthBuy + arbitrageEvent.DepthSell) / 2;

        // Categories to update
        var metricKeys = new List<(string Category, string Key)>
        {
            ("Pair", arbitrageEvent.Pair),
            ("Hour", $"{dayShort}-{hour:D2}"),
            ("Day", dayLong),
            ("Direction", arbitrageEvent.Direction),
            ("Global", "Total")
        };

        foreach (var (category, key) in metricKeys)
        {
            var metricId = $"{category}:{key}";
            var metric = await dbContext.AggregatedMetrics.FirstOrDefaultAsync(m => m.Id == metricId);

            if (metric == null)
            {
                metric = new AggregatedMetric
                {
                    Id = metricId,
                    Category = category,
                    MetricKey = key,
                    EventCount = 1,
                    SumSpread = spreadPercent,
                    MaxSpread = spreadPercent,
                    SumDepth = avgDepth,
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.AggregatedMetrics.Add(metric);
            }
            else
            {
                metric.EventCount++;
                metric.SumSpread += spreadPercent;
                metric.SumDepth += avgDepth;
                metric.LastUpdated = DateTime.UtcNow;

                if (spreadPercent > metric.MaxSpread)
                {
                    metric.MaxSpread = spreadPercent;
                }
            }
        }
    }
}
