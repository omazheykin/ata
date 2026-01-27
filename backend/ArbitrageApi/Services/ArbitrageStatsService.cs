using System.Threading.Channels;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services;

public class ArbitrageStatsService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ArbitrageStatsService> _logger;
    private readonly Channel<ArbitrageEvent> _eventChannel;

    public ArbitrageStatsService(IServiceProvider serviceProvider, ILogger<ArbitrageStatsService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _eventChannel = Channel.CreateUnbounded<ArbitrageEvent>();
    }

    public async ValueTask QueueEventAsync(ArbitrageEvent arbitrageEvent)
    {
        await _eventChannel.Writer.WriteAsync(arbitrageEvent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📊 Arbitrage Stats Service started");

        await foreach (var arbitrageEvent in _eventChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                
                dbContext.ArbitrageEvents.Add(arbitrageEvent);
                await dbContext.SaveChangesAsync(stoppingToken);
                
                _logger.LogDebug("Saved arbitrage event for {Pair}", arbitrageEvent.Pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving arbitrage event");
            }
        }
    }

    public async Task<StatsResponse> GetStatsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        var events = await dbContext.ArbitrageEvents.ToListAsync();
        
        var response = new StatsResponse();
        
        if (!events.Any()) return response;

        // 1. Summary by Pair
        response.Summary.Pairs = events.GroupBy(e => e.Pair)
            .ToDictionary(g => g.Key, g => new PairStats
            {
                Count = g.Count(),
                AvgSpread = g.Average(e => e.Spread),
                MaxSpread = g.Max(e => e.Spread)
            });

        // 2. Summary by Hour
        response.Summary.Hours = events.GroupBy(e => e.Timestamp.Hour)
            .ToDictionary(g => g.Key, g => new HourStats
            {
                Count = g.Count(),
                AvgSpread = g.Average(e => e.Spread),
                MaxSpread = g.Max(e => e.Spread),
                AvgDepth = g.Average(e => (e.DepthBuy + e.DepthSell) / 2)
            });

        // 3. Summary by Day
        response.Summary.Days = events.GroupBy(e => e.Timestamp.DayOfWeek.ToString())
            .ToDictionary(g => g.Key, g => new DayStats
            {
                Count = g.Count(),
                AvgSpread = g.Average(e => e.Spread)
            });

        // 4. Direction Distribution
        response.Summary.DirectionDistribution = events.GroupBy(e => e.Direction)
            .ToDictionary(g => g.Key, g => g.Count());

        // 5. Avg Series Duration
        var sortedEvents = events.OrderBy(e => e.Timestamp).ToList();
        if (sortedEvents.Any())
        {
            var seriesLengths = new List<int>();
            int currentSeriesLength = 1;
            for (int i = 1; i < sortedEvents.Count; i++)
            {
                if (sortedEvents[i].Direction == sortedEvents[i - 1].Direction)
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
        foreach (var day in days)
        {
            var dayHours = new Dictionary<string, HourDetail>();
            for (int h = 0; h < 24; h++)
            {
                var hourEvents = events.Where(e => e.Timestamp.DayOfWeek.ToString() == day && e.Timestamp.Hour == h).ToList();
                
                var detail = new HourDetail();
                if (hourEvents.Any())
                {
                    detail.AvgOpportunitiesPerHour = hourEvents.Count; // Simplified for now, could be averaged over weeks
                    detail.AvgSpread = hourEvents.Average(e => e.Spread);
                    detail.AvgDepth = hourEvents.Average(e => (e.DepthBuy + e.DepthSell) / 2);
                    detail.DirectionBias = hourEvents.GroupBy(e => e.Direction)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
                    detail.VolatilityScore = CalculateVolatilityScore(hourEvents, events);
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

        response.Summary.GlobalVolatilityScore = CalculateVolatilityScore(events, events);

        return response;
    }

    private decimal CalculateVolatilityScore(List<ArbitrageEvent> hourEvents, List<ArbitrageEvent> allEvents)
    {
        if (!hourEvents.Any()) return 0;

        // Normalized Count Score (compared to max hourly count)
        var hourlyGroups = allEvents.GroupBy(e => new { e.Timestamp.Date, e.Timestamp.Hour }).ToList();
        var maxHourlyCount = hourlyGroups.Any() ? hourlyGroups.Max(g => g.Count()) : 1;
        var countScore = (decimal)hourEvents.Count / maxHourlyCount;

        // Normalized Spread Score (target 1% as "max")
        var avgSpread = hourEvents.Average(e => e.Spread);
        var spreadScore = Math.Min(avgSpread / 0.01m, 1.0m);

        // Normalized Depth Score (target 1000 as "max")
        var avgDepth = hourEvents.Average(e => (e.DepthBuy + e.DepthSell) / 2);
        var depthScore = Math.Min(avgDepth / 1000m, 1.0m);

        // Stability Score (1 - direction switches / total)
        int switches = 0;
        for (int i = 1; i < hourEvents.Count; i++)
        {
            if (hourEvents[i].Direction != hourEvents[i - 1].Direction) switches++;
        }
        var stabilityScore = 1.0m - ((decimal)switches / hourEvents.Count);

        // Weighted average
        return Math.Min((countScore * 0.4m) + (spreadScore * 0.3m) + (depthScore * 0.2m) + (stabilityScore * 0.1m), 1.0m);
    }
}
