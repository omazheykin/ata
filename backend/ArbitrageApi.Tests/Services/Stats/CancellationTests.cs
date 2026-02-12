using System.Collections.Generic;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Stats;
using ArbitrageApi.Services.Stats.Processors;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services.Stats;

public class CancellationTests
{
    private readonly Mock<ILogger<ArbitrageStatsService>> _loggerMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private StatsDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StatsDbContext(options);
    }

    [Fact]
    public async Task BaseAggregator_ShouldRespectCancellationToken()
    {
        // Arrange
        var aggregator = new HourAggregator(); // Subclass of BaseAggregator
        var db = GetInMemoryDbContext();
        var ev = new ArbitrageEvent { Timestamp = DateTime.UtcNow };
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => 
            await aggregator.UpdateMetricsAsync(ev, db, cts.Token));
    }

    [Fact]
    public async Task HeatmapProcessor_ShouldRespectCancellationToken()
    {
        // Arrange
        var processor = new HeatmapProcessor();
        var db = GetInMemoryDbContext();
        var ev = new ArbitrageEvent { Timestamp = DateTime.UtcNow };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => 
            await processor.ProcessAsync(ev, db, cts.Token));
    }

    [Fact]
    public async Task PersistenceProcessor_ShouldRespectCancellationToken()
    {
        // Arrange
        var processor = new PersistenceProcessor();
        var db = GetInMemoryDbContext();
        var ev = new ArbitrageEvent { Id = Guid.NewGuid(), Pair = "BTCUSDT" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => 
            await processor.ProcessAsync(ev, db, cts.Token));
    }

    [Fact]
    public async Task ArbitrageStatsService_ShouldExitCleanly_WhenCanceled()
    {
        // Arrange
        var channelProvider = new ChannelProvider();
        
        var service = new ArbitrageStatsService(
            null!,
            _loggerMock.Object,
            channelProvider,
            null!,
            null!);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act & Assert (Should not throw, should complete task)
        var task = service.StartAsync(cts.Token);
        await task; 

        Assert.True(task.IsCompleted);
    }
}
