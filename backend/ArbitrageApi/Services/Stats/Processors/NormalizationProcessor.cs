using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats.Processors;

public class NormalizationProcessor : IEventProcessor
{
    public Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        // 1. Calculate Spread Percentage (displayed as 0.57 instead of 0.0057)
        arbitrageEvent.SpreadPercent = arbitrageEvent.Spread * 100;

        // 2. Ensure Timestamp is UTC (backend standard)
        if (arbitrageEvent.Timestamp.Kind != DateTimeKind.Utc)
        {
            arbitrageEvent.Timestamp = DateTime.SpecifyKind(arbitrageEvent.Timestamp, DateTimeKind.Utc);
        }

        return Task.CompletedTask;
    }
}
