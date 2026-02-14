using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services.Stats;

public class HistoricalAnalysisServiceTests
{
    private StatsDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StatsDbContext(options);
    }

    [Fact]
    public async Task CalculateVolatilityIndexAsync_ShouldReturnCorrectStandardDeviation()
    {
        // Arrange
        using var db = GetInMemoryDbContext();
        var pair = "BTC-USDT";
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;

        db.ArbitrageEvents.AddRange(new List<ArbitrageEvent>
        {
            new() { Pair = pair, SpreadPercent = 1.0m, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new() { Pair = pair, SpreadPercent = 2.0m, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new() { Pair = pair, SpreadPercent = 3.0m, Timestamp = DateTime.UtcNow.AddMinutes(-1) }
        });
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<HistoricalAnalysisService>>().Object;
        var service = new HistoricalAnalysisService(db, logger);

        // Act
        var volatility = await service.CalculateVolatilityIndexAsync(pair, start, end);

        // Assert
        // Standard Deviation of [1, 2, 3] is ~1.0
        Assert.InRange(volatility, 0.9, 1.1);
    }

    [Fact]
    public async Task GetTopProfitablePairsAsync_ShouldRankCorrectly()
    {
        // Arrange
        using var db = GetInMemoryDbContext();
        db.Transactions.AddRange(new List<Transaction>
        {
            new() { Asset = "BTC", Pair = "BTC-USDT", RealizedProfit = 100m, Status = "Success", Type = "Arbitrage", Timestamp = DateTime.UtcNow },
            new() { Asset = "ETH", Pair = "ETH-USDT", RealizedProfit = 200m, Status = "Success", Type = "Arbitrage", Timestamp = DateTime.UtcNow }
        });
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<HistoricalAnalysisService>>().Object;
        var service = new HistoricalAnalysisService(db, logger);

        // Act
        var stats = await service.GetTopProfitablePairsAsync();

        // Assert
        Assert.Equal("ETH-USDT", stats[0].Pair);
        Assert.Equal(200m, stats[0].TotalProfit);
        Assert.Equal("BTC-USDT", stats[1].Pair);
    }
}
