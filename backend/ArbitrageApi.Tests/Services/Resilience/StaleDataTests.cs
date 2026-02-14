using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Configuration;
using ArbitrageApi.Services.Stats;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Reflection;
using ArbitrageApi.Hubs;

namespace ArbitrageApi.Tests.Services.Resilience;

public class StaleDataTests
{
    private readonly Mock<ILogger<ArbitrageDetectionService>> _detectorLoggerMock;
    private readonly Mock<IBookProvider> _binanceProviderMock;
    private readonly Mock<IBookProvider> _coinbaseProviderMock;
    private readonly Mock<IHubContext<ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly DepthThresholdService _depthThresholds;
    private readonly PairsConfigRoot _pairsConfig;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;

    public StaleDataTests()
    {
        _detectorLoggerMock = new Mock<ILogger<ArbitrageDetectionService>>();
        _binanceProviderMock = new Mock<IBookProvider>();
        _coinbaseProviderMock = new Mock<IBookProvider>();
        _binanceProviderMock.Setup(p => p.ExchangeName).Returns("Binance");
        _coinbaseProviderMock.Setup(p => p.ExchangeName).Returns("Coinbase");

        _hubContextMock = new Mock<IHubContext<ArbitrageHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        _channelProvider = new ChannelProvider();
        _pairsConfig = new PairsConfigRoot();
        _pairsConfig.Pairs.Add(new PairConfig { Symbol = "BTC", TechnicalMinDepth = 0.001 });

        var mockCache = new Mock<CalendarCache>();
        _depthThresholds = new DepthThresholdService(mockCache.Object, _pairsConfig);
        
        _persistenceServiceMock = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
        _persistenceServiceMock.Setup(p => p.GetState()).Returns(new AppState());
    }

    [Fact]
    public async Task DetectForPairAsync_WithStaleData_ShouldSkipDetection()
    {
        // Arrange
        var providers = new List<IBookProvider> { _binanceProviderMock.Object, _coinbaseProviderMock.Object };
        var service = new ArbitrageDetectionService(
            providers,
            _hubContextMock.Object,
            _detectorLoggerMock.Object,
            _channelProvider,
            _depthThresholds,
            _pairsConfig,
            _persistenceServiceMock.Object
        );

        var now = DateTime.UtcNow;
        var staleTimestamp = now.AddSeconds(-1); // 1s ago (threshold is 500ms)

        _binanceProviderMock.Setup(p => p.GetOrderBook("BTC"))
            .Returns((new List<(decimal, decimal)> { (50000m, 1.0m) }, new List<(decimal, decimal)> { (50010m, 1.0m) }, staleTimestamp));
        
        _coinbaseProviderMock.Setup(p => p.GetOrderBook("BTC"))
            .Returns((new List<(decimal, decimal)> { (50000m, 1.0m) }, new List<(decimal, decimal)> { (50010m, 1.0m) }, now));

        // Act
        // Invoke private method DetectForPairAsync via reflection
        var method = typeof(ArbitrageDetectionService).GetMethod("DetectForPairAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)method.Invoke(service, new object[] { _pairsConfig.Pairs[0], now, CancellationToken.None });

        // Assert
        // Check if any opportunity was written to the channel - it should be empty
        _channelProvider.TradeChannel.Reader.Count.Should().Be(0);
        
        // Also check if warning was logged
        _detectorLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Stale order book detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TradeService_WithStaleData_ShouldAbortTrade()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TradeService>>();
        var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var mockSection = new Mock<Microsoft.Extensions.Configuration.IConfigurationSection>();
        mockSection.Setup(s => s.Value).Returns((string?)null);
        configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(mockSection.Object);
        var statsServiceMock = new Mock<ArbitrageStatsService>(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<ArbitrageStatsService>>().Object,
            _channelProvider,
            null!, // RebalancingService
            null!); // StatsBootstrapService
        var clients = new List<IExchangeClient>(); // Empty list instead of null
        var rebalancingServiceMock = new Mock<RebalancingService>(
            new Mock<ILogger<RebalancingService>>().Object,
            clients,
            new Mock<ITrendAnalysisService>().Object,
            _channelProvider,
            _persistenceServiceMock.Object,
            _hubContextMock.Object);
        var executionServiceMock = new Mock<OrderExecutionService>(
            new Mock<ILogger<OrderExecutionService>>().Object,
            clients,
            _channelProvider,
            _hubContextMock.Object);

        var calculator = new ArbitrageCalculator(new Mock<ILogger<ArbitrageCalculator>>().Object);
        var providers = new List<IBookProvider> { _binanceProviderMock.Object, _coinbaseProviderMock.Object };

        var now = DateTime.UtcNow;
        var staleTimestamp = now.AddSeconds(-2);
        
        _persistenceServiceMock.Setup(p => p.GetState()).Returns(new AppState { IsAutoTradeEnabled = true });

        var service = new TradeService(
            loggerMock.Object,
            configMock.Object,
            _persistenceServiceMock.Object,
            _hubContextMock.Object,
            _channelProvider,
            statsServiceMock.Object,
            rebalancingServiceMock.Object,
            executionServiceMock.Object,
            calculator,
            providers,
            clients
        );

        // Setup stale data for one exchange
        _binanceProviderMock.Setup(p => p.GetOrderBook("BTC"))
            .Returns((new List<(decimal, decimal)> { (50000m, 1.0m) }, new List<(decimal, decimal)> { (50010m, 1.0m) }, staleTimestamp));
        _coinbaseProviderMock.Setup(p => p.GetOrderBook("BTC"))
            .Returns((new List<(decimal, decimal)> { (50000m, 1.0m) }, new List<(decimal, decimal)> { (50010m, 1.0m) }, now));

        var opp = new ArbitrageOpportunity
        {
            Symbol = "BTC",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            ProfitPercentage = 1.0m
        };

        // Act
        // We can't easily wait for BackgroundService loop, but we can test the internal logic 
        // if we use reflection or if we trigger the channel.
        // Let's use reflection to call the private ProcessTradeSignalsAsync or similar?
        // Actually, TradeService logic is in ProcessTradeSignalsAsync which reads from channel.
        
        // For a clean test, let's use reflection to call the inner validation logic if it was a separate method.
        // Looking at TradeService.cs, it's all inside the loop.
        // I'll use reflection to call the private method.
        
        var method = typeof(TradeService).GetMethod("ProcessTradeSignalsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Start the task and then write to channel
        var cts = new CancellationTokenSource();
        var task = (Task)method.Invoke(service, new object[] { cts.Token });
        
        await _channelProvider.TradeChannel.Writer.WriteAsync(opp);
        
        // Wait a bit for processing
        await Task.Delay(100);
        cts.Cancel();

        // Assert
        executionServiceMock.Verify(e => e.ExecuteTradeAsync(It.IsAny<ArbitrageOpportunity>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Trade aborted: Stale data")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}
