using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;
using ArbitrageApi.Services.Stats;

namespace ArbitrageApi.Services.Stats.Processors;

// This is a simplified placeholder as we may not want a separate table for Every single summary stat.
// For now, focusing on making the Heatmap work first as it's the user's primary concern.
public class SummaryProcessor : IEventProcessor
{
    private readonly IStatsAggregator _aggregator;

    public SummaryProcessor(IStatsAggregator aggregator)
    {
        _aggregator = aggregator;
    }

    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        await _aggregator.UpdateMetricsAsync(arbitrageEvent, dbContext);
    }
}
