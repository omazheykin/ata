using System.Threading.Channels;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Stats;
using Microsoft.AspNetCore.SignalR;
using ArbitrageApi.Services.Stats.Processors;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services;

public class ArbitrageStatsService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ArbitrageStatsService> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly RebalancingService _rebalancingService;
    private readonly SemaphoreSlim _updateTrigger = new(0, 1);
    private readonly Type[] _parallelProcessors;

    public ArbitrageStatsService(
        IServiceProvider serviceProvider, 
        ILogger<ArbitrageStatsService> logger, 
        ChannelProvider channelProvider, 
        RebalancingService rebalancingService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _channelProvider = channelProvider;
        _rebalancingService = rebalancingService;

        // Define parallel processors (to be executed via Task.WhenAll)
        _parallelProcessors = new[]
        {
            typeof(PersistenceProcessor),
            typeof(HeatmapProcessor),
            typeof(SummaryProcessor),
            typeof(BroadcastProcessor)
        };
    }

    public void TriggerUpdate()
    {
        try
        {
            if (_updateTrigger.CurrentCount == 0)
                _updateTrigger.Release();
        }
        catch (ObjectDisposedException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📊 Arbitrage Stats Service starting...");

        // Ensure database is created
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                await dbContext.Database.EnsureCreatedAsync(stoppingToken);
                _logger.LogInformation("✅ Database initialized successfully.");

                // Phase 6: Bootstrap aggregation if missing
                await BootstrapAggregationAsync(dbContext, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Could not initialize database during startup. It might be locked or inaccessible. Continuing without persistence...");
        }

        var eventsTask = ProcessEventsAsync(stoppingToken);
        var transactionsTask = ProcessTransactionsAsync(stoppingToken);
        var strategyTask = ProcessStrategyUpdatesAsync(stoppingToken);

        await Task.WhenAll(eventsTask, transactionsTask, strategyTask);
    }

    private async Task ProcessStrategyUpdatesAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Ensure background processing starts
        _logger.LogInformation("🧠 SMART STRATEGY: Loop is now ACTIVE and running.");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var day = now.DayOfWeek.ToString();
                var hour = now.Hour;

                _logger.LogInformation("🧠 SMART STRATEGY: Evaluating market conditions for {Day} {Hour}:00...", day, hour);

                var response = await GetStatsAsync();
                var currentHourDetail = response.Calendar.GetValueOrDefault(day.Substring(0, 3))?.GetValueOrDefault(hour.ToString("D2"));

                var state = _serviceProvider.GetRequiredService<StatePersistenceService>().GetState();
                if (!state.IsSmartStrategyEnabled)
                {
                    _logger.LogInformation("🧠 Strategy Update: Smart Strategy is DISABLED. Skipping update.");
                    await _updateTrigger.WaitAsync(TimeSpan.FromMinutes(15), stoppingToken);
                    continue;
                }

                decimal newThreshold = 0.1m; // Default
                string reason = "Standard market conditions";
                decimal volScore = 0;
                decimal cScore = 0;
                decimal sScore = 0;

                if (currentHourDetail != null)
                {
                    volScore = currentHourDetail.VolatilityScore;
                    // For the current hour update, we'll re-calculate to get the breakdown if needed, 
                    // or just use the available VolatilityScore.
                    // To get the reason more granular, let's use the VolatilityScore.

                    if (volScore >= 0.7m)
                    {
                        newThreshold = 0.05m;
                        reason = $"High activity (Score: {volScore:P0}). Opportunities are frequent and spreads are wide, allowing for a lower capture threshold.";
                    }
                    else if (volScore < 0.2m)
                    {
                        newThreshold = 0.15m;
                        reason = $"Quiet market (Score: {volScore:P0}). Low frequency or narrow spreads detected; threshold is raised to avoid unprofitable trades.";
                    }
                    else
                    {
                        reason = $"Balanced conditions (Score: {volScore:P0}). Market activity is moderate; system is using a standard {newThreshold:P1} target.";
                    }
                }
                else
                {
                    reason = $"Initial assessment. Insufficient historical data for {day} {hour}:00; using conservative {newThreshold:P1} base threshold.";
                }

                _logger.LogInformation("🧠 SMART STRATEGY: Pushing new threshold {Threshold}% to Detection Service. Reason: {Reason}", newThreshold, reason);
                
                await _channelProvider.StrategyUpdateChannel.Writer.WriteAsync(new StrategyUpdate
                {
                    MinProfitThreshold = newThreshold,
                    Reason = reason,
                    VolatilityScore = volScore,
                    CountScore = cScore, // We'll leave these for now or refactor GetStats to include them
                    SpreadScore = sScore
                }, stoppingToken);

                // Wait for next hour or 15 mins for re-evaluation OR manual trigger
                await _updateTrigger.WaitAsync(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Strategy Update Loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ProcessEventsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var arbitrageEvent in _channelProvider.EventChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // 1. Normalization (Sequential Enrichment)
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var normalizationProcessor = scope.ServiceProvider.GetRequiredService<NormalizationProcessor>();
                        await normalizationProcessor.ProcessAsync(arbitrageEvent, scope.ServiceProvider.GetRequiredService<StatsDbContext>());
                    }

                    // 2. Parallel Processing for independent tasks
                    var parallelTasks = _parallelProcessors.Select(async processorType =>
                    {
                        try
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var processor = (IEventProcessor)scope.ServiceProvider.GetRequiredService(processorType);
                            var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                            await processor.ProcessAsync(arbitrageEvent, dbContext);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in parallel processor {ProcessorType}", processorType.Name);
                        }
                    });

                    await Task.WhenAll(parallelTasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in event processing chain");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessTransactionsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var transaction in _channelProvider.TransactionChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("💰 Stats Service: Received transaction for {Asset}. Saving to DB...", transaction.Asset);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                    
                    dbContext.Transactions.Add(transaction);
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving transaction");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<List<ArbitrageEvent>> GetEventsByPairAsync(string pair)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        _logger.LogInformation("📊 GetEventsByPairAsync called for pair: {Pair}", pair);
        
        // Get all events to check what pairs exist in the database
        var allPairs = await dbContext.ArbitrageEvents
            .Select(e => e.Pair)
            .Distinct()
            .ToListAsync();
        
        _logger.LogInformation("📊 Available pairs in database: {Pairs}", string.Join(", ", allPairs));

        // Use case-insensitive comparison
        var events = await dbContext.ArbitrageEvents
            .Where(e => e.Pair.ToUpper() == pair.ToUpper())
            .OrderByDescending(e => e.Timestamp)
            .Take(100)
            .ToListAsync();
        
        _logger.LogInformation("📊 Found {Count} events for pair {Pair}", events.Count, pair);
        
        return events;
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

    public virtual async Task<StatsResponse> GetStatsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        var metrics = await dbContext.AggregatedMetrics.ToListAsync();
        var response = new StatsResponse();

        if (!metrics.Any()) return response;

        // 1. Summary by Pair
        response.Summary.Pairs = metrics
            .Where(m => m.Category == "Pair")
            .ToDictionary(m => m.MetricKey, m => new PairStats
            {
                Count = m.EventCount,
                AvgSpread = m.SumSpread / m.EventCount / 100,
                MaxSpread = m.MaxSpread / 100
            });

        // 2. Summary by Hour (MetricKey is "Mon-12" etc)
        // Note: HourStats in model uses 'int Count' but persistent key is string.
        // We'll map the "Hour" category (which is per-day-hour) to the general DaySummary.
        var hourMetrics = metrics.Where(m => m.Category == "Hour").ToList();
        
        // The frontend expects Summary.Hours as Dictionary<int, HourStats> for the 0-23 summary
        response.Summary.Hours = hourMetrics
            .GroupBy(m => int.Parse(m.MetricKey.Split('-')[1]))
            .ToDictionary(g => g.Key, g => new HourStats
            {
                Count = g.Sum(m => m.EventCount),
                AvgSpread = g.Sum(m => m.SumSpread) / g.Sum(m => m.EventCount) / 100,
                MaxSpread = g.Max(m => m.MaxSpread) / 100,
                AvgDepth = g.Sum(m => m.SumDepth) / g.Sum(m => m.EventCount)
            });

        // 3. Summary by Day
        response.Summary.Days = metrics
            .Where(m => m.Category == "Day")
            .ToDictionary(m => m.MetricKey, m => new DayStats
            {
                Count = m.EventCount,
                AvgSpread = m.SumSpread / m.EventCount / 100
            });

        // 4. Direction Distribution - Still requires scanning events for now unless we aggregate it too.
        // Let's quickly aggregate it too for completeness.
        var directionMetrics = metrics.Where(m => m.Category == "Direction").ToList();
        if (directionMetrics.Any())
        {
            response.Summary.DirectionDistribution = directionMetrics
                .ToDictionary(m => m.MetricKey, m => m.EventCount);
        }
        else
        {
            // Fallback for bootstrap that didn't have Directions (I'll update aggregator next)
            _logger.LogWarning("⚠️ Direction metrics missing in aggregation. Consider re-bootstrap.");
        }

        // 5. Avg Series Duration - This one actually REQUIRES raw events to calculate properly.
        // Process only last 1000 events for this to keep it fast.
        var latestEvents = await dbContext.ArbitrageEvents
            .OrderByDescending(e => e.Timestamp)
            .Take(1000)
            .ToListAsync();

        if (latestEvents.Any())
        {
            var seriesLengths = new List<int>();
            int currentSeriesLength = 1;
            for (int i = 1; i < latestEvents.Count; i++)
            {
                if (latestEvents[i].Direction == latestEvents[i - 1].Direction)
                {
                    currentSeriesLength++;
                }
                else
                {
                    seriesLengths.Add(currentSeriesLength);
                    currentSeriesLength = 1;
                }
            }
            seriesLengths.Add(currentSeriesLength);
            response.Summary.AvgSeriesDuration = seriesLengths.Average();
        }

        // 6. Calendar Logic
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var maxHourlyCount = hourMetrics.Any() ? hourMetrics.Max(m => m.EventCount) : 1;

        foreach (var day in days)
        {
            var dayHours = new Dictionary<string, HourDetail>();
            var dayShort = day.Substring(0, 3);
            
            for (int h = 0; h < 24; h++)
            {
                var hourId = $"{dayShort}-{h:D2}";
                var metric = hourMetrics.FirstOrDefault(m => m.MetricKey == hourId);
                
                var detail = new HourDetail();
                if (metric != null)
                {
                    detail.AvgOpportunitiesPerHour = metric.EventCount; 
                    detail.Count = metric.EventCount;
                    detail.AvgSpread = metric.SumSpread / metric.EventCount / 100;
                    detail.MaxSpread = metric.MaxSpread / 100;
                    detail.AvgDepth = metric.SumDepth / metric.EventCount;
                    detail.DirectionBias = "N/A"; // Bias would need direction counts
                    detail.VolatilityScore = CalculateVolatilityScoreFast(metric, maxHourlyCount);
                    detail.Zone = detail.VolatilityScore >= 0.7m ? "high_activity" : (detail.VolatilityScore >= 0.4m ? "normal" : "low_activity");
                }
                else
                {
                    detail.Zone = "low_activity";
                }
                dayHours[h.ToString("D2")] = detail;
            }
            response.Calendar[day.Substring(0, 3)] = dayHours;
        }

        response.Summary.GlobalVolatilityScore = CalculateVolatilityScoreFast(
            metrics.FirstOrDefault(m => m.Id == "Global:Total"), maxHourlyCount);

        // 7. Rebalancing Info
        var skews = _rebalancingService.GetAllSkews();
        response.Rebalancing.AssetSkews = skews;
        
        if (skews.Any())
        {
            var maxSkew = skews.Values.Max(Math.Abs);
            var worstAsset = skews.OrderByDescending(x => Math.Abs(x.Value)).First();
            
            if (maxSkew > 0.3m)
            {
                response.Rebalancing.Recommendation = worstAsset.Value > 0 
                    ? $"Withdraw {worstAsset.Key} from Binance to Coinbase" 
                    : $"Withdraw {worstAsset.Key} from Coinbase to Binance";
            }
            else
            {
                response.Rebalancing.Recommendation = "Inventories are well-balanced.";
            }
            
            response.Rebalancing.EfficiencyScore = 1.0m - maxSkew;
        }

        return response;
    }

    private decimal CalculateVolatilityScoreFast(AggregatedMetric? metric, int maxHourlyCount)
    {
        if (metric == null || metric.EventCount == 0) return 0;

        // Normalized Count Score
        var countScore = (decimal)metric.EventCount / maxHourlyCount;

        // Normalized Spread Score
        var avgSpread = metric.SumSpread / metric.EventCount / 100;
        var spreadScore = Math.Min(avgSpread / 0.01m, 1.0m);

        // Normalized Depth Score
        var avgDepth = metric.SumDepth / metric.EventCount;
        var depthScore = Math.Min(avgDepth / 1000m, 1.0m);

        // Stability Score - We can't do this purely from sums without extra counters.
        // For now, assume moderate stability (0.5) to keep it simple.
        var stabilityScore = 0.5m;

        return Math.Min((countScore * 0.4m) + (spreadScore * 0.3m) + (depthScore * 0.2m) + (stabilityScore * 0.1m), 1.0m);
    }

    public async Task<HeatmapCell?> GetCellDetailsAsync(string day, int hour)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
        
        var cellId = $"{day}-{hour:D2}";
        return await dbContext.HeatmapCells.FirstOrDefaultAsync(c => c.Id == cellId);
    }

    public async Task<List<ArbitrageEvent>> GetCellEventsAsync(string day, int hour)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
        
        var targetDay = ParseDayOfWeek(day);
        
        return await dbContext.ArbitrageEvents
            .Where(e => (int)e.Timestamp.DayOfWeek == (int)targetDay && e.Timestamp.Hour == hour)
            .OrderByDescending(e => e.Timestamp)
            .Take(100)
            .ToListAsync();
    }

    public async Task<List<ArbitrageEvent>> GetAllCellEventsAsync(string day, int hour)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
        
        var targetDay = ParseDayOfWeek(day);
        
        return await dbContext.ArbitrageEvents
            .Where(e => (int)e.Timestamp.DayOfWeek == (int)targetDay && e.Timestamp.Hour == hour)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    private DayOfWeek ParseDayOfWeek(string day)
    {
        return day.ToUpper() switch
        {
            "MON" => DayOfWeek.Monday,
            "TUE" => DayOfWeek.Tuesday,
            "WED" => DayOfWeek.Wednesday,
            "THU" => DayOfWeek.Thursday,
            "FRI" => DayOfWeek.Friday,
            "SAT" => DayOfWeek.Saturday,
            "SUN" => DayOfWeek.Sunday,
            _ => throw new ArgumentException($"Invalid day: {day}")
        };
    }
}
