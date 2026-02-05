using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Tests.Services;

public class TradeServiceTests
{
    private readonly Mock<ILogger<TradeService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;
    private readonly Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<ArbitrageStatsService> _statsServiceMock;
    private readonly Mock<RebalancingService> _rebalancingServiceMock;
    private readonly TradeService _tradeService;

    public TradeServiceTests()
    {
        _loggerMock = new Mock<ILogger<TradeService>>();
        
        var myConfiguration = new Dictionary<string, string?> {
            {"Trading:MinProfitThreshold", "0.5"},
            {"Trading:ExecutionStrategy", "Sequential"}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();

        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();

        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");

        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };

        // Initialize Mocks for Concrete Dependencies
        _persistenceServiceMock = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
        _persistenceServiceMock.Setup(x => x.GetState()).Returns(new AppState());

        _hubContextMock = new Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        _channelProvider = new ChannelProvider();

        var trendServiceMock = new Mock<ITrendAnalysisService>();
        _rebalancingServiceMock = new Mock<RebalancingService>(
            new Mock<ILogger<RebalancingService>>().Object, 
            new List<IExchangeClient>(),
            trendServiceMock.Object,
            _channelProvider);
        _rebalancingServiceMock.Setup(x => x.GetSkew(It.IsAny<string>())).Returns(0m);

        // Pass nulls to base constructor of StatsService as we mock the method we need
        _statsServiceMock = new Mock<ArbitrageStatsService>(null!, null!, null!, null!, null!);
        _statsServiceMock.Setup(x => x.GetStatsAsync()).ReturnsAsync(new StatsResponse());

        _tradeService = new TradeService(
            _loggerMock.Object,
            clients,
            _configuration,
            _persistenceServiceMock.Object,
            _hubContextMock.Object,
            _channelProvider,
            _statsServiceMock.Object,
            _rebalancingServiceMock.Object
        );
    }

    [Fact]
    public async Task ExecuteSequential_Success_ShouldPlaceBothOrders()
    {
        // Arrange
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 51000m });

        // Act
        var result = await _tradeService.ExecuteTradeAsync(opportunity);

        // Assert
        result.Should().BeTrue();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync("BTCUSD", 0.1m), Times.Once);
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once);
    }

    [Fact]
    public async Task ExecuteSequential_SecondFail_ShouldTriggerUndo()
    {
        // Arrange
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed });

        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled });

        // Act
        var result = await _tradeService.ExecuteTradeAsync(opportunity);

        // Assert
        result.Should().BeFalse();
        _binanceClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once); // Undo order
        var transactions = _tradeService.GetRecentTransactions();
        transactions[0].Status.Should().Be("Recovered");
    }

    [Fact]
    public async Task ExecuteConcurrent_Success_ShouldPlaceBothOrdersSimultaneously()
    {
        // Arrange
        _tradeService.SetExecutionStrategy(ExecutionStrategy.Concurrent);
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 51000m });

        // Act
        var result = await _tradeService.ExecuteTradeAsync(opportunity);

        // Assert
        result.Should().BeTrue();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync("BTCUSD", 0.1m), Times.Once);
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once);
    }

    [Fact]
    public async Task SlippageCheck_ShouldAbortIfSpreadTooNarrow()
    {
        // Arrange
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        _binanceClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50000m });

        _coinbaseClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50100m }); // 0.2% spread

        _tradeService.SetMinProfitThreshold(0.5m); // Threshold is higher than current spread

        // Act
        var result = await _tradeService.ExecuteTradeAsync(opportunity);

        // Assert
        result.Should().BeFalse();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }
}
