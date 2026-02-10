using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Services.Stats;
using ArbitrageApi.Services.Strategies;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class TradeServiceTests
{
    private readonly Mock<ILogger<TradeService>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<StatePersistenceService> _persistenceMock;
    private readonly Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<ArbitrageStatsService> _statsMock;
    private readonly Mock<RebalancingService> _rebalancingMock;
    private readonly Mock<OrderExecutionService> _executionMock;
    private readonly TradeService _service;

    public TradeServiceTests()
    {
        _loggerMock = new Mock<ILogger<TradeService>>();
        _configMock = new Mock<IConfiguration>();
        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(s => s.Value).Returns((string?)null);
        _configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(mockSection.Object);
        
        // Setup StatePersistence mock
        var state = new AppState { IsAutoTradeEnabled = true, MinProfitThreshold = 0.5m };
        _persistenceMock = new Mock<StatePersistenceService>(
            new Mock<ILogger<StatePersistenceService>>().Object);
        _persistenceMock.Setup(p => p.GetState()).Returns(state);

        _hubContextMock = new Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        _channelProvider = new ChannelProvider();
        
        // Mock other services
        _statsMock = new Mock<ArbitrageStatsService>(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<ArbitrageStatsService>>().Object,
            _channelProvider,
            null!, // RebalancingService
            null!); // StatsBootstrapService
        _statsMock.Setup(s => s.GetStatsAsync()).ReturnsAsync(new StatsResponse());

        _rebalancingMock = new Mock<RebalancingService>(
            new Mock<ILogger<RebalancingService>>().Object,
            new List<IExchangeClient>(),
            new Mock<ITrendAnalysisService>().Object,
            _channelProvider,
            _persistenceMock.Object,
            _hubContextMock.Object);

        _executionMock = new Mock<OrderExecutionService>(
            new Mock<ILogger<OrderExecutionService>>().Object,
            new List<IExchangeClient>(),
            _channelProvider,
            _hubContextMock.Object);

        _service = new TradeService(
            _loggerMock.Object,
            _configMock.Object,
            _persistenceMock.Object,
            _hubContextMock.Object,
            _channelProvider,
            _statsMock.Object,
            _rebalancingMock.Object,
            _executionMock.Object);
    }

    /// <summary>
    /// Verifies that if Auto-Trade is disabled in the settings, the service correctly 
    /// skips all incoming trade signals even if they are profitable.
    /// </summary>
    [Fact]
    public async Task ProcessTradeSignals_ShouldSkip_WhenAutoTradeDisabled()
    {
        // Arrange
        _service.SetAutoTrade(false);
        var opportunity = new ArbitrageOpportunity { Symbol = "BTCUSD", ProfitPercentage = 1.0m };
        var cts = new CancellationTokenSource(100);

        // Act
        // We need to start the service or manually call the internal loop if accessible, 
        // but since it's a BackgroundService, we'll write to the channel and wait.
        // For testing, we might want to expose the processing logic or use a derived class.
        // Let's use reflection or a small hack to call the private method for direct testing.
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity);
        
        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _executionMock.Verify(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that if the global Safety Kill-Switch is triggered, no trades are executed.
    /// This is the highest priority filter to prevent losses during market crashes or API issues.
    /// </summary>
    [Fact]
    public async Task ProcessTradeSignals_ShouldBlock_WhenSafetyKillSwitchTriggered()
    {
        // Arrange
        var state = _persistenceMock.Object.GetState();
        state.IsSafetyKillSwitchTriggered = true;
        _service.SetAutoTrade(true);
        
        var opportunity = new ArbitrageOpportunity { Symbol = "BTCUSD", ProfitPercentage = 1.0m };
        var cts = new CancellationTokenSource(100);
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity);

        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _executionMock.Verify(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that opportunities with profit percentages below the global minimum 
    /// threshold are filtered out and not sent for execution.
    /// </summary>
    [Fact]
    public async Task ProcessTradeSignals_ShouldSkip_WhenBelowThreshold()
    {
        // Arrange
        _service.SetAutoTrade(true);
        _service.SetMinProfitThreshold(1.0m); // Threshold 1.0%
        
        var opportunity = new ArbitrageOpportunity { Symbol = "BTCUSD", ProfitPercentage = 0.5m }; // Opportunity 0.5%
        var cts = new CancellationTokenSource(100);
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity);

        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _executionMock.Verify(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the "happy path": when auto-trade is enabled, no kill-switch is active,
    /// and profit is above threshold, the signal is correctly routed to OrderExecutionService.
    /// </summary>
    [Fact]
    public async Task ProcessTradeSignals_ShouldExecute_WhenFiltersPass()
    {
        // Arrange
        _service.SetAutoTrade(true);
        _service.SetMinProfitThreshold(0.1m);
        
        var opportunity = new ArbitrageOpportunity { Symbol = "BTCUSD", ProfitPercentage = 0.5m };
        var cts = new CancellationTokenSource(100);
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        _executionMock.Setup(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity);

        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _executionMock.Verify(e => e.ExecuteTradeAsync(It.Is<ArbitrageOpportunity>(o => o.Symbol == "BTCUSD"), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that pair-specific profit thresholds (e.g. for ETHUSD) correctly 
    /// override the global default threshold.
    /// </summary>
    [Fact]
    public async Task ProcessTradeSignals_ShouldRespectPairSpecificThreshold()
    {
        // Arrange
        var state = _persistenceMock.Object.GetState();
        state.MinProfitThreshold = 0.5m; // Global
        state.PairThresholds["ETHUSD"] = 1.0m; // Specific
        
        _service.SetAutoTrade(true);
        
        var opportunity = new ArbitrageOpportunity { Symbol = "ETHUSD", ProfitPercentage = 0.7m }; // 0.7 < 1.0, should skip
        var cts = new CancellationTokenSource(100);
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await _channelProvider.TradeChannel.Writer.WriteAsync(opportunity);

        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _executionMock.Verify(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    /// <summary>
    /// Verifies that automated rebalancing signals are processed and executed 
    /// when the necessary trend and viability criteria are met.
    /// </summary>
    [Fact]
    public async Task ProcessRebalanceSignals_ShouldExecute_WhenCriteriaMet()
    {
        // Arrange
        var state = _persistenceMock.Object.GetState();
        state.IsAutoRebalanceEnabled = true;

        var proposal = new RebalancingProposal 
        { 
            Asset = "BTC", 
            TrendDescription = "Strong Upward Trend", // Strong trend
            IsViable = true 
        };
        var cts = new CancellationTokenSource(100);
        var method = typeof(TradeService).GetMethod("ProcessRebalanceSignalsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await _channelProvider.RebalanceChannel.Writer.WriteAsync(proposal);

        // Act
        var task = method?.Invoke(_service, new object[] { cts.Token }) as Task;
        if (task != null) await Task.Delay(50);
        cts.Cancel();

        // Assert
        _rebalancingMock.Verify(r => r.ExecuteRebalanceAsync(It.Is<RebalancingProposal>(p => p.Asset == "BTC"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
