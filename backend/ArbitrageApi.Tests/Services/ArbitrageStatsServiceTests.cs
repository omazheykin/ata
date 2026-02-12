using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class ArbitrageStatsServiceTests
{
    private readonly Mock<ILogger<ArbitrageStatsService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly RebalancingService _rebalancingMock;
    private readonly StatsBootstrapService _bootstrapMock;
    private readonly ChannelProvider _channelProvider;
    public ArbitrageStatsServiceTests()
    {
        _loggerMock = new Mock<ILogger<ArbitrageStatsService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _channelProvider = new ChannelProvider();
        _rebalancingMock = null!;
        _bootstrapMock = null!;
    }

    private StatsDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new StatsDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private ArbitrageStatsService CreateService(StatsDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var serviceProvider = services.BuildServiceProvider();

        _serviceProviderMock.Setup(s => s.GetService(typeof(IServiceScopeFactory)))
            .Returns(serviceProvider.GetRequiredService<IServiceScopeFactory>());

        return new ArbitrageStatsService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _channelProvider,
            _rebalancingMock,
            _bootstrapMock
        );
    }

    [Fact]
    public async Task GetCellDetailsAsync_ShouldReturnAccurateSummaryFromEvents()
    {
        // Arrange
        var dbContext = GetDbContext();
        var ts = new DateTime(2026, 2, 2, 12, 30, 0); // Monday, 12:30
        
        dbContext.ArbitrageEvents.AddRange(new List<ArbitrageEvent>
        {
            new ArbitrageEvent { Id = Guid.NewGuid(), Pair = "BTCUSDT", SpreadPercent = 1.0m, Timestamp = ts, DayOfWeek = (int)DayOfWeek.Monday, Hour = 12, Direction = "B→C" },
            new ArbitrageEvent { Id = Guid.NewGuid(), Pair = "ETHUSDT", SpreadPercent = 2.0m, Timestamp = ts.AddMinutes(1), DayOfWeek = (int)DayOfWeek.Monday, Hour = 12, Direction = "C→B" },
            new ArbitrageEvent { Id = Guid.NewGuid(), Pair = "XRPUSDT", SpreadPercent = 3.0m, Timestamp = ts.AddMinutes(2), DayOfWeek = (int)DayOfWeek.Monday, Hour = 12, Direction = "B→C" }
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        // Act
        var result = await service.GetCellDetailsAsync("MON", 12);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.EventCount);
        Assert.Equal(2.0m, result.AvgSpread); // (1+2+3)/3
        Assert.Equal(3.0m, result.MaxSpread);
        Assert.Equal("B→C", result.DirectionBias);
    }

    [Fact]
    public async Task GetCellDetailsAsync_EmptyCell_ShouldReturnEmptySummary()
    {
        // Arrange
        var dbContext = GetDbContext();
        var service = CreateService(dbContext);

        // Act
        var result = await service.GetCellDetailsAsync("TUE", 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.EventCount);
    }
}
