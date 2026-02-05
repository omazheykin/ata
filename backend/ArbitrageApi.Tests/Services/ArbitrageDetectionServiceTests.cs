using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services
{
    public class ArbitrageDetectionServiceTests
    {
        private readonly Mock<IHubContext<ArbitrageHub>> _mockHubContext;
        private readonly Mock<IHubClients> _mockHubClients;
        private readonly Mock<IClientProxy> _mockClientProxy;
        private readonly Mock<ILogger<ArbitrageDetectionService>> _mockLogger;
        private readonly Mock<StatePersistenceService> _mockPersistenceService;
        private readonly ChannelProvider _channelProvider;
        private readonly ArbitrageCalculator _calculator;

        public ArbitrageDetectionServiceTests()
        {
            _mockHubContext = new Mock<IHubContext<ArbitrageHub>>();
            _mockHubClients = new Mock<IHubClients>();
            _mockClientProxy = new Mock<IClientProxy>();
            _mockLogger = new Mock<ILogger<ArbitrageDetectionService>>();
            _mockPersistenceService = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
            _channelProvider = new ChannelProvider();
            _calculator = new ArbitrageCalculator(new Mock<ILogger<ArbitrageCalculator>>().Object);

            // Setup Hub Mocks
            _mockHubContext.Setup(h => h.Clients).Returns(_mockHubClients.Object);
            _mockHubClients.Setup(c => c.All).Returns(_mockClientProxy.Object);

            // Setup Persistence Mock
            _mockPersistenceService.Setup(p => p.GetState()).Returns(new AppState { MinProfitThreshold = 0.1m, IsSmartStrategyEnabled = false });
        }

        private ArbitrageDetectionService CreateService()
        {
            return new ArbitrageDetectionService(
                _mockHubContext.Object,
                _mockLogger.Object,
                new List<IBookProvider>(),
                new List<IExchangeClient>(),
                _channelProvider,
                _calculator,
                _mockPersistenceService.Object,
                null // ArbitrageStatsService is not used in ProcessOpportunityAsync
            );
        }

        [Fact]
        public async Task ProcessOpportunityAsync_ShouldNotBroadcast_WhenValueIsBelowThreshold()
        {
            // Arrange
            var service = CreateService();
            var opportunity = new ArbitrageOpportunity
            {
                Symbol = "ETHUSDT",
                BuyExchange = "Binance",
                SellExchange = "Coinbase",
                BuyPrice = 1000m,
                Volume = 0.05m, // Value = 50 < 100
                ProfitPercentage = 1.0m,
                GrossProfitPercentage = 1.0m
            };

            // Act
            var methodInfo = typeof(ArbitrageDetectionService).GetMethod("ProcessOpportunityAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (methodInfo == null) throw new InvalidOperationException("Method not found");
            await (Task)methodInfo.Invoke(service, new object[] { opportunity, "ETHUSDT", CancellationToken.None })!;

            // Assert
            // Verify ReceiveOpportunity was NOT called
            _mockClientProxy.Verify(
                c => c.SendCoreAsync("ReceiveOpportunity", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ProcessOpportunityAsync_ShouldBroadcast_WhenValueIsAboveThreshold()
        {
            // Arrange
            var service = CreateService();
            var opportunity = new ArbitrageOpportunity
            {
                Symbol = "ETHUSDT",
                BuyExchange = "Binance",
                SellExchange = "Coinbase",
                BuyPrice = 1000m,
                Volume = 0.15m, // Value = 150 > 100
                ProfitPercentage = 1.0m,
                GrossProfitPercentage = 1.0m
            };

            // Act
            var methodInfo = typeof(ArbitrageDetectionService).GetMethod("ProcessOpportunityAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (methodInfo == null) throw new InvalidOperationException("Method not found");
            await (Task)methodInfo.Invoke(service, new object[] { opportunity, "ETHUSDT", CancellationToken.None })!;

            // Assert
            // Verify ReceiveOpportunity WAS called (checking via SendCoreAsync which SendAsync calls internally)
            _mockClientProxy.Verify(
                c => c.SendCoreAsync("ReceiveOpportunity", It.Is<object[]>(o => ((ArbitrageOpportunity)o[0]).Volume == 0.15m), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
