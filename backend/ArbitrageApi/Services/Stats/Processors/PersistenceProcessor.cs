using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats.Processors;

public class PersistenceProcessor : IEventProcessor
{
    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        dbContext.ArbitrageEvents.Add(arbitrageEvent);
        await dbContext.SaveChangesAsync(ct);
    }
}
