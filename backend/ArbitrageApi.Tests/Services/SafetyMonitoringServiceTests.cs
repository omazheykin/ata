using ArbitrageApi.Data;
using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class SafetyMonitoringServiceTests
{
    private readonly Mock<ILogger<SafetyMonitoringService>> _loggerMock;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;
    private readonly Mock<IHubContext<ArbitrageHub>> _hubContextMock;
    private readonly IServiceProvider _serviceProvider;
    private readonly SafetyMonitoringService _safetyService;
    private readonly StatsDbContext _dbContext;

    public SafetyMonitoringServiceTests()
    {
        _loggerMock = new Mock<ILogger<SafetyMonitoringService>>();
        _persistenceServiceMock = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
        _hubContextMock = new Mock<IHubContext<ArbitrageHub>>();

        // Mock SignalR
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        // Setup InMemory DB
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new StatsDbContext(options);

        var services = new ServiceCollection();
        services.AddSingleton(_dbContext);
        _serviceProvider = services.BuildServiceProvider();

        _safetyService = new SafetyMonitoringService(
            _loggerMock.Object,
            _serviceProvider,
            _persistenceServiceMock.Object,
            _hubContextMock.Object,
            null! // ArbitrageDetectionService not needed for these tests
        );
    }

    [Fact]
    public async Task CheckSafetyLimits_ConsecutiveLosses_ShouldTriggerKillSwitch()
    {
        // Arrange
        var state = new AppState { 
            IsAutoTradeEnabled = true, 
            MaxConsecutiveLosses = 3,
            MaxDrawdownUsd = 1000m
        };
        _persistenceServiceMock.Setup(p => p.GetState()).Returns(state);

        // Add 3 failed transactions
        _dbContext.Transactions.AddRange(
            new Transaction { Id = Guid.NewGuid(), Status = "Failed", Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new Transaction { Id = Guid.NewGuid(), Status = "Failed", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new Transaction { Id = Guid.NewGuid(), Status = "Partial", Timestamp = DateTime.UtcNow.AddMinutes(-1) }
        );
        await _dbContext.SaveChangesAsync();

        // Act - Invoke the private method using reflection for testing
        var method = typeof(SafetyMonitoringService).GetMethod("CheckSafetyLimitsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_safetyService, new object[] { state, CancellationToken.None })!;

        // Assert
        state.IsAutoTradeEnabled.Should().BeFalse();
        state.IsSafetyKillSwitchTriggered.Should().BeTrue();
        state.GlobalKillSwitchReason.Should().Contain("Consecutive failures detected");
    }

    [Fact]
    public async Task CheckSafetyLimits_DrawdownLimit_ShouldTriggerKillSwitch()
    {
        // Arrange
        var state = new AppState { 
            IsAutoTradeEnabled = true, 
            MaxConsecutiveLosses = 10,
            MaxDrawdownUsd = 50.0m
        };
        _persistenceServiceMock.Setup(p => p.GetState()).Returns(state);

        // Add successful trades with negative profit (loss)
        _dbContext.Transactions.AddRange(
            new Transaction { Id = Guid.NewGuid(), Status = "Success", Profit = -30m, Timestamp = DateTime.UtcNow.AddHours(-1) },
            new Transaction { Id = Guid.NewGuid(), Status = "Success", Profit = -30m, Timestamp = DateTime.UtcNow.AddHours(-2) }
        ); // Total loss = $60 > $50 limit
        await _dbContext.SaveChangesAsync();

        // Act
        var method = typeof(SafetyMonitoringService).GetMethod("CheckSafetyLimitsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_safetyService, new object[] { state, CancellationToken.None })!;

        // Assert
        state.IsAutoTradeEnabled.Should().BeFalse();
        state.IsSafetyKillSwitchTriggered.Should().BeTrue();
        state.GlobalKillSwitchReason.Should().Contain("Max daily drawdown reached");
    }

    [Fact]
    public async Task CheckSafetyLimits_WithinLimits_ShouldNotTrigger()
    {
        // Arrange
        var state = new AppState { 
            IsAutoTradeEnabled = true, 
            MaxConsecutiveLosses = 3,
            MaxDrawdownUsd = 100.0m
        };
        _persistenceServiceMock.Setup(p => p.GetState()).Returns(state);

        // 1 success, 1 fail (not consecutive failures)
        _dbContext.Transactions.AddRange(
            new Transaction { Id = Guid.NewGuid(), Status = "Success", Profit = 10m, Timestamp = DateTime.UtcNow.AddMinutes(-1) },
            new Transaction { Id = Guid.NewGuid(), Status = "Failed", Timestamp = DateTime.UtcNow.AddMinutes(-2) }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var method = typeof(SafetyMonitoringService).GetMethod("CheckSafetyLimitsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_safetyService, new object[] { state, CancellationToken.None })!;

        // Assert
        state.IsAutoTradeEnabled.Should().BeTrue();
        state.IsSafetyKillSwitchTriggered.Should().BeFalse();
    }
}
