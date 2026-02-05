using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public interface IEventProcessor
{
    Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext);
}
