using ArbitrageApi.Data;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class CompositeStatsAggregator : IStatsAggregator
{
    private readonly IEnumerable<IStatsAggregator> _aggregators;

    public CompositeStatsAggregator(IEnumerable<IStatsAggregator> aggregators)
    {
        // Filter out self to avoid infinite recursion if misconfigured
        _aggregators = aggregators.Where(a => a != this).ToList();
    }

    public async Task UpdateMetricsAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext, CancellationToken ct)
    {
        foreach (var aggregator in _aggregators)
        {
            await aggregator.UpdateMetricsAsync(arbitrageEvent, dbContext, ct);
        }
    }
}
