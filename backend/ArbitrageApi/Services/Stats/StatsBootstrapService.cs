using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats;

public class StatsBootstrapService
{
    private readonly ILogger<StatsBootstrapService> _logger;

    public StatsBootstrapService(ILogger<StatsBootstrapService> logger)
    {
        _logger = logger;
    }

    public async Task BootstrapAggregationAsync(StatsDbContext dbContext, CancellationToken stoppingToken)
    {
        if (await dbContext.AggregatedMetrics.AnyAsync(stoppingToken))
        {
            _logger.LogInformation("📊 Aggregated metrics already exist. Skipping bootstrap.");
            return;
        }

        _logger.LogInformation("📊 Bootstrapping aggregated metrics from existing events...");
        
        var totalEvents = await dbContext.ArbitrageEvents.CountAsync(stoppingToken);
        _logger.LogInformation("📊 Total events to process: {Total}", totalEvents);
        
        // Load all metrics into memory for fast lookups
        var metricsCache = new Dictionary<string, AggregatedMetric>();
        
        int processed = 0;
        int batchSize = 5000; // Larger batches for better performance

        while (true)
        {
            // Stable sort is CRITICAL here to prevent row skipping
            var events = await dbContext.ArbitrageEvents
                .AsNoTracking()
                .OrderBy(e => e.Timestamp)
                .ThenBy(e => e.Id)
                .Skip(processed)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (events.Count == 0) break;

            foreach (var e in events)
            {
                // Process this event into aggregated metrics (in-memory)
                var timestamp = e.Timestamp;
                var dayLong = timestamp.DayOfWeek.ToString();
                var dayShort = dayLong.Substring(0, 3);
                var hour = timestamp.Hour;
                var spreadPercent = e.Spread * 100;
                var avgDepth = (e.DepthBuy + e.DepthSell) / 2;

                var metricKeys = new List<(string Category, string Key)>
                {
                    ("Pair", e.Pair),
                    ("Hour", $"{dayShort}-{hour:D2}"),
                    ("Day", dayLong),
                    ("Direction", e.Direction),
                    ("Global", "Total")
                };

                foreach (var (category, key) in metricKeys)
                {
                    var metricId = $"{category}:{key}";
                    
                    if (!metricsCache.TryGetValue(metricId, out var metric))
                    {
                        metric = new AggregatedMetric
                        {
                            Id = metricId,
                            Category = category,
                            MetricKey = key,
                            EventCount = 0,
                            SumSpread = 0,
                            MaxSpread = 0,
                            SumDepth = 0,
                            LastUpdated = DateTime.UtcNow
                        };
                        metricsCache[metricId] = metric;
                    }

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

            processed += events.Count;
            
            if (processed % 10000 == 0 || processed == totalEvents)
            {
                _logger.LogInformation("📊 Progress: {Processed}/{Total} events processed", processed, totalEvents);
            }
        }

        // Save all metrics to database in one go
        _logger.LogInformation("📊 Saving {Count} aggregated metrics to database...", metricsCache.Count);
        dbContext.AggregatedMetrics.AddRange(metricsCache.Values);
        await dbContext.SaveChangesAsync(stoppingToken);

        _logger.LogInformation("✅ Aggregated metrics bootstrap complete. Created {Count} metrics from {Total} events.", 
            metricsCache.Count, totalEvents);
    }
}
