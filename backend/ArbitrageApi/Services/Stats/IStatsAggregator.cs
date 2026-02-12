using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public interface IStatsAggregator
{
    Task UpdateMetricsAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext, CancellationToken ct);
}
