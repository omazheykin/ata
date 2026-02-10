using System.Globalization;
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
        // Don't check for existing metrics here anymore, as we support partial rebuilds/merges
        _logger.LogInformation("📊 Bootstrapping aggregated metrics from existing events...");
        
        var totalEvents = await dbContext.ArbitrageEvents.CountAsync(stoppingToken);
        _logger.LogInformation("📊 Total events to process: {Total}", totalEvents);
        
        // 1. In-Memory Aggregation First (Fastest)
        var metricsCache = new Dictionary<string, AggregatedMetric>();
        var heatmapCache = new Dictionary<string, HeatmapCell>();
        
        int processed = 0;
        int batchSize = 10000; // Larger batch for reading

        while (true)
        {
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
                var timestamp = e.Timestamp;
                var dayLong = timestamp.DayOfWeek.ToString();
                var dayShort = timestamp.ToString("ddd", CultureInfo.InvariantCulture);
                var hour = timestamp.Hour;
                var spreadPercent = e.Spread * 100;
                var avgDepth = (e.DepthBuy + e.DepthSell) / 2;

                // 1. Aggregated Metrics
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
                            Id = metricId, Category = category, MetricKey = key,
                            LastUpdated = DateTime.UtcNow
                        };
                        metricsCache[metricId] = metric;
                    }
                    metric.EventCount++;
                    metric.SumSpread += spreadPercent;
                    metric.SumDepth += avgDepth;
                    if (spreadPercent > metric.MaxSpread) metric.MaxSpread = spreadPercent;
                }

                // 2. Heatmap Cells
                var cellId = $"{dayShort}-{hour:D2}";
                if (!heatmapCache.TryGetValue(cellId, out var cell))
                {
                    cell = new HeatmapCell
                    {
                        Id = cellId,
                        Day = dayShort,
                        Hour = hour,
                        EventCount = 1,
                        AvgSpread = spreadPercent, // Initialize with first value
                        MaxSpread = spreadPercent,
                        DirectionBias = e.Direction
                    };
                    heatmapCache[cellId] = cell;
                }
                else
                {
                    // Correct incremental avg formula
                    cell.AvgSpread = (cell.AvgSpread * cell.EventCount + spreadPercent) / (cell.EventCount + 1);
                    cell.EventCount++;
                    if (spreadPercent > cell.MaxSpread) cell.MaxSpread = spreadPercent;
                }
            }

            processed += events.Count;
            if (processed % 20000 == 0 || processed == totalEvents)
            {
                _logger.LogInformation("📊 Progress: {Processed}/{Total} events processed in memory", processed, totalEvents);
            }
        }

        // 2. Save to Database with Merge Logic (Handle existing records)
        _logger.LogInformation("📊 Saving {MCount} metrics and {HCount} heatmap cells with MERGE logic...", metricsCache.Count, heatmapCache.Count);
        
        // Save Metrics in batches
        await MergeMetricsAsync(dbContext, metricsCache.Values.ToList(), stoppingToken);
        
        // Save Heatmap Cells in batches
        await MergeHeatmapCellsAsync(dbContext, heatmapCache.Values.ToList(), stoppingToken);

        _logger.LogInformation("✅ Aggregated metrics bootstrap complete. Processed {Total} events.", totalEvents);
    }

    private async Task MergeMetricsAsync(StatsDbContext dbContext, List<AggregatedMetric> metrics, CancellationToken stoppingToken)
    {
        const int dbBatchSize = 500; // Small batch for DB operations to avoid locking
        for (int i = 0; i < metrics.Count; i += dbBatchSize)
        {
            var batch = metrics.Skip(i).Take(dbBatchSize).ToList();
            var batchIds = batch.Select(m => m.Id).ToList();
            
            // Load existing records to merge
            var existingMetrics = await dbContext.AggregatedMetrics
                .Where(m => batchIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m, stoppingToken);

            foreach (var newMetric in batch)
            {
                if (existingMetrics.TryGetValue(newMetric.Id, out var existing))
                {
                    // Merge fields
                    existing.EventCount += newMetric.EventCount;
                    existing.SumSpread += newMetric.SumSpread;
                    existing.SumDepth += newMetric.SumDepth;
                    if (newMetric.MaxSpread > existing.MaxSpread) existing.MaxSpread = newMetric.MaxSpread;
                    existing.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    // Insert new
                    dbContext.AggregatedMetrics.Add(newMetric);
                }
            }
            await dbContext.SaveChangesAsync(stoppingToken); // Commit batch
        }
    }

    private async Task MergeHeatmapCellsAsync(StatsDbContext dbContext, List<HeatmapCell> cells, CancellationToken stoppingToken)
    {
        const int dbBatchSize = 500;
        for (int i = 0; i < cells.Count; i += dbBatchSize)
        {
            var batch = cells.Skip(i).Take(dbBatchSize).ToList();
            var batchIds = batch.Select(c => c.Id).ToList();

            var existingCells = await dbContext.HeatmapCells
                .Where(c => batchIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c, stoppingToken);

            foreach (var newCell in batch)
            {
                if (existingCells.TryGetValue(newCell.Id, out var existing))
                {
                    // Merge heatmap data (weighted average)
                    var totalCount = existing.EventCount + newCell.EventCount;
                    var weightedSum = (existing.AvgSpread * existing.EventCount) + (newCell.AvgSpread * newCell.EventCount);
                    
                    existing.AvgSpread = weightedSum / totalCount;
                    existing.EventCount = totalCount;
                    if (newCell.MaxSpread > existing.MaxSpread) existing.MaxSpread = newCell.MaxSpread;
                }
                else
                {
                    dbContext.HeatmapCells.Add(newCell);
                }
            }
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
