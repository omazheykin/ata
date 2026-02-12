using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Stats;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArbitrageApi.Tests.Services.Stats;

public class StatsAggregatorTests
{
    private StatsDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new StatsDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task UpdateMetricsAsync_ShouldInitializeNewMetrics()
    {
        // Arrange
        var dbContext = GetDbContext();
        var aggregators = new List<IStatsAggregator>
        {
            new HourAggregator(),
            new DayAggregator(),
            new PairAggregator(),
            new GlobalAggregator(),
            new DirectionAggregator()
        };
        var aggregator = new CompositeStatsAggregator(aggregators);
        var arbitrageEvent = new ArbitrageEvent
        {
            Pair = "BTCUSDT",
            Direction = "B→C",
            Spread = 0.01m, // 1%
            DepthBuy = 100,
            DepthSell = 200,
            Timestamp = new DateTime(2026, 2, 2, 12, 0, 0) // Monday
        };

        // Act
        await aggregator.UpdateMetricsAsync(arbitrageEvent, dbContext, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        // Assert
        var pairMetric = await dbContext.AggregatedMetrics.FindAsync("Pair:BTCUSDT");
        Assert.NotNull(pairMetric);
        Assert.Equal(1, pairMetric.EventCount);
        Assert.Equal(1.0m, pairMetric.SumSpread);
        Assert.Equal(150m, pairMetric.SumDepth);

        var hourMetric = await dbContext.AggregatedMetrics.FindAsync("Hour:Mon-12");
        Assert.NotNull(hourMetric);
        Assert.Equal(1, hourMetric.EventCount);

        var globalMetric = await dbContext.AggregatedMetrics.FindAsync("Global:Total");
        Assert.NotNull(globalMetric);
        Assert.Equal(1, globalMetric.EventCount);
    }

    [Fact]
    public async Task UpdateMetricsAsync_ShouldIncrementExistingMetrics()
    {
        // Arrange
        var dbContext = GetDbContext();
        var aggregators = new List<IStatsAggregator>
        {
            new HourAggregator(),
            new DayAggregator(),
            new PairAggregator(),
            new GlobalAggregator(),
            new DirectionAggregator()
        };
        var aggregator = new CompositeStatsAggregator(aggregators);
        var ts = new DateTime(2026, 2, 2, 12, 0, 0);
        
        var event1 = new ArbitrageEvent
        {
            Pair = "BTCUSDT",
            Direction = "B→C",
            Spread = 0.01m,
            DepthBuy = 100,
            DepthSell = 100,
            Timestamp = ts
        };
        
        var event2 = new ArbitrageEvent
        {
            Pair = "BTCUSDT",
            Direction = "B→C",
            Spread = 0.02m,
            DepthBuy = 200,
            DepthSell = 200,
            Timestamp = ts
        };

        // Act
        await aggregator.UpdateMetricsAsync(event1, dbContext, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await aggregator.UpdateMetricsAsync(event2, dbContext, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        // Assert
        var pairMetric = await dbContext.AggregatedMetrics.FindAsync("Pair:BTCUSDT");
        Assert.Equal(2, pairMetric.EventCount);
        Assert.Equal(3.0m, pairMetric.SumSpread); // 1% + 2%
        Assert.Equal(2.0m, pairMetric.MaxSpread);
        Assert.Equal(300m, pairMetric.SumDepth); // 100 + 200
    }
}
