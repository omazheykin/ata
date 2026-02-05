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
    private readonly StatsBootstrapService _bootstrapService;
    private readonly Type[] _parallelProcessors;

    public ArbitrageStatsService(
        IServiceProvider serviceProvider, 
        ILogger<ArbitrageStatsService> logger, 
        ChannelProvider channelProvider, 
        RebalancingService rebalancingService,
        StatsBootstrapService bootstrapService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _channelProvider = channelProvider;
        _rebalancingService = rebalancingService;
        _bootstrapService = bootstrapService;

        // Define parallel processors (to be executed via Task.WhenAll)
        _parallelProcessors = new[]
        {
            typeof(PersistenceProcessor),
            typeof(HeatmapProcessor),
            typeof(SummaryProcessor)
        };
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

                // Phase 6: Bootstrap aggregation if missing (Delegate to Service)
                await _bootstrapService.BootstrapAggregationAsync(dbContext, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Could not initialize database during startup. It might be locked or inaccessible. Continuing without persistence...");
        }

        var eventsTask = ProcessEventsAsync(stoppingToken);
        var transactionsTask = ProcessTransactionsAsync(stoppingToken);

        await Task.WhenAll(eventsTask, transactionsTask);
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

    // BootstrapAggregationAsync REMOVED (Extracted to StatsBootstrapService)

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

        // 4. Direction Distribution
        var directionMetrics = metrics.Where(m => m.Category == "Direction").ToList();
        if (directionMetrics.Any())
        {
            response.Summary.DirectionDistribution = directionMetrics
                .ToDictionary(m => m.MetricKey, m => m.EventCount);
        }
        else
        {
            _logger.LogWarning("⚠️ Direction metrics missing in aggregation. Consider re-bootstrap.");
        }

        // 5. Avg Series Duration
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
                    detail.DirectionBias = "N/A";
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
        response.Rebalancing.Proposals = _rebalancingService.GetProposals();
        
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

        // Stability Score
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
