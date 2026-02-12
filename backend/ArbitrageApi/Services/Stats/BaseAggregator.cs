using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats;

public abstract class BaseAggregator : IStatsAggregator
{
    protected abstract (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent);

    public async Task UpdateMetricsAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (category, key) = GetMetricKey(arbitrageEvent);
        if (string.IsNullOrEmpty(key)) return;

        var metricId = $"{category}:{key}";
        var spreadPercent = arbitrageEvent.Spread * 100;
        var avgDepth = (arbitrageEvent.DepthBuy + arbitrageEvent.DepthSell) / 2;

        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var metric = await dbContext.AggregatedMetrics.FirstOrDefaultAsync(m => m.Id == metricId, ct);

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

                await dbContext.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
            {
                foreach (var entry in dbContext.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }
                await Task.Delay(10 * (int)Math.Pow(2, attempt), ct);
            }
        }
    }
}
