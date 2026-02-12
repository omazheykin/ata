using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Threading.Channels;

namespace ArbitrageApi.Services.Stats;

public class CalendarStatsService : BackgroundService
{
    private readonly ChannelProvider _channels;
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
        _channels = channels;
        _reader = channels.CalendarStats.Reader;
        _cache = cache;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("📅 Calendar Stats Service started.");

        try
        {
            await foreach (var ev in _reader.ReadAllAsync(token))
            {
                // 1. Update In-Memory Cache (Real-time)
                _cache.AddEvent(ev);

                // 2. Emit to EventChannel for full processing (Persistence, Heatmap, Summary)
                try 
                {
                    var arbEvent = new ArbitrageEvent
                    {
                        Id = Guid.NewGuid(),
                        Pair = ev.Pair,
                        Spread = (decimal)ev.Spread, // Gross Spread
                        SpreadPercent = (decimal)(ev.Spread * 100),
                        DepthBuy = (decimal)ev.Depth, 
                        DepthSell = (decimal)ev.Depth, 
                        Timestamp = ev.TimestampUtc,
                        DayOfWeek = (int)ev.TimestampUtc.DayOfWeek,
                        Hour = ev.TimestampUtc.Hour,
                        Direction = ev.Direction 
                    };

                    _channels.EventChannel.Writer.TryWrite(arbEvent);
                    
                    if (string.IsNullOrEmpty(ev.Direction))
                    {
                        _logger.LogWarning("⚠️ Emitted event for {Pair} with EMPTY direction!", ev.Pair);
                    }
                    else 
                    {
                        _logger.LogDebug("📡 Emitted event for {Pair} with direction: {Direction}", ev.Pair, ev.Direction);
                    }
                }
                catch (OperationCanceledException) { throw; } // Propagate to outer catch
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing calendar event");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("📅 Calendar Stats Service stopping...");
        }
    }
}
