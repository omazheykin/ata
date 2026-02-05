using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats.Processors;

public class PersistenceProcessor : IEventProcessor
{
    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        dbContext.ArbitrageEvents.Add(arbitrageEvent);
        await dbContext.SaveChangesAsync();
    }
}
