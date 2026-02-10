using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Threading.Channels;

namespace ArbitrageApi.Services.Stats;

public class CalendarStatsService : BackgroundService
{
    private readonly ChannelReader<CalendarEvent> _reader;
    private readonly CalendarCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CalendarStatsService> _logger;

    public CalendarStatsService(
        ChannelProvider channels, 
        CalendarCache cache, 
        IServiceProvider serviceProvider,
        ILogger<CalendarStatsService> logger)
    {
        _reader = channels.CalendarStats.Reader;
        _cache = cache;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("📅 Calendar Stats Service started.");

        await foreach (var ev in _reader.ReadAllAsync(token))
        {
            // 1. Update In-Memory Cache (Real-time)
            _cache.AddEvent(ev);

            // 2. Persist to DB
            try 
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                
                var arbEvent = new ArbitrageEvent
                {
                    Id = Guid.NewGuid(),
                    Pair = ev.Pair,
                    Spread = (decimal)ev.Spread, // Gross Spread
                    SpreadPercent = (decimal)(ev.Spread * 100),
                    DepthBuy = (decimal)ev.Depth, // We only have 'Depth' (min), so store it in both or just one?
                    DepthSell = (decimal)ev.Depth, // Storing min depth in both for now as approximation
                    Timestamp = ev.TimestampUtc,
                    Direction = "N/A" // Direction info not strictly in CalendarEvent, but harmless
                };

                db.ArbitrageEvents.Add(arbEvent);
                
                // Optional: Update AggregatedMetrics roughly if needed, 
                // but preserving raw history is the "Record all" requirement.
                
                await db.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing calendar event");
            }
        }
    }
}
