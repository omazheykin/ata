using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class OrderExecutionServiceTests
{
    private readonly Mock<ILogger<OrderExecutionService>> _loggerMock;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly OrderExecutionService _service;

    public OrderExecutionServiceTests()
    {
        _loggerMock = new Mock<ILogger<OrderExecutionService>>();
        
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");
        
        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        
        _hubContextMock = new Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);
        
        _channelProvider = new ChannelProvider();

        _service = new OrderExecutionService(
            _loggerMock.Object,
            clients,
            _channelProvider,
            _hubContextMock.Object
        );
    }

    [Fact]
    public async Task ExecuteSequential_Success_ShouldPlaceBothOrders()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        // Mock Prices for Slippage Check
        _binanceClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50000m });
        _coinbaseClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 51000m });

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 51000m });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeTrue();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync("BTCUSD", 0.1m), Times.Once);
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once);
    }

    [Fact]
    public async Task ExecuteSequential_SecondFail_ShouldTriggerUndo()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
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
            .ReturnsAsync(new ExchangePrice { Price = 51000m });

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed });

        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeFalse();
        _binanceClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once); // Undo order
    }

    [Fact]
    public async Task ExecuteConcurrent_Success_ShouldPlaceBothOrdersSimultaneously()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Concurrent);
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
            .ReturnsAsync(new ExchangePrice { Price = 51000m });

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 51000m });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeTrue();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync("BTCUSD", 0.1m), Times.Once);
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.1m), Times.Once);
    }

    [Fact]
    public async Task SlippageCheck_ShouldAbortIfSpreadTooNarrow()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
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

        // Act
        // Threshold 0.5% > Spread 0.2%
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeFalse();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }
    [Fact]
    public async Task ExecuteSequential_PnLCheck_ShouldCalculateRealizedProfitCorrectly()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m,
            BuyFee = 0.001m, // 0.1%
            SellFee = 0.001m // 0.1%
        };

        // Current Prices for Slippage Check
        _binanceClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50000m });
        _coinbaseClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50200m });

        // Execution Results
        // Buy: 0.1 BTC @ 50,000. Cost = 5,000. Fee (default 0.1%) = 5.
        // Sell: 0.1 BTC @ 50,200. Proceeds = 5,020. Fee (default 0.1%) = 5.02.
        // Gross Profit: 5020 - 5000 = 20.
        // Total Fees: 5 + 5.02 = 10.02.
        // Realized Profit = 20 - 10.02 = 9.98.

        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50000m });

        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.1m, Price = 50200m });

        // Act
        await _service.ExecuteTradeAsync(opportunity, 0.1m);

        // Assert
        var transactions = _service.GetRecentTransactions();
        transactions.Should().NotBeEmpty();
        var t = transactions.OrderByDescending(x => x.Timestamp).First();

        t.BuyCost.Should().Be(5000m);
        t.SellProceeds.Should().Be(5020m);
        t.TotalFees.Should().Be(10.02m);
        t.RealizedProfit.Should().Be(9.98m);
    }
}
