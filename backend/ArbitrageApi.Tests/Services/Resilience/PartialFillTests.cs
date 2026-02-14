using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services.Resilience;

public class PartialFillTests
{
    private readonly Mock<ILogger<OrderExecutionService>> _loggerMock;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly Mock<IHubContext<ArbitrageApi.Hubs.ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly OrderExecutionService _service;

    public PartialFillTests()
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
    public async Task ExecuteSequential_Leg1PartialFill_ShouldAdjustLeg2Volume()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 1.0m // Requesting 1.0 BTC
        };

        // Mock Prices for Slippage Check (bypass)
        _binanceClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50000m });
        _coinbaseClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 51000m });

        // Leg 1: Partially filled (only 0.4 BTC available)
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse { 
                Status = OrderStatus.PartiallyFilled, 
                ExecutedQuantity = 0.4m, 
                Price = 50000m 
            });

        // Leg 2: Should only try to sell 0.4 BTC
        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.4m))
            .ReturnsAsync(new OrderResponse { 
                Status = OrderStatus.Filled, 
                ExecutedQuantity = 0.4m, 
                Price = 51000m 
            });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeTrue();
        _binanceClientMock.Verify(c => c.PlaceMarketBuyOrderAsync("BTCUSD", 1.0m), Times.Once);
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.4m), Times.Once);

        var transactions = _service.GetRecentTransactions();
        transactions.First().Amount.Should().Be(0.4m);
        transactions.First().Status.Should().Be("Success");
    }

    [Fact]
    public async Task ExecuteSequential_Leg1Partial_Leg2Fail_ShouldTriggerUndoWithPartialVolume()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
        var opportunity = new ArbitrageOpportunity
        {
            Asset = "BTC",
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 1.0m
        };

        _binanceClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 50000m });
        _coinbaseClientMock.Setup(c => c.GetPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(new ExchangePrice { Price = 51000m });

        // Leg 1: Partial Fill (0.2 BTC)
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse { 
                Status = OrderStatus.PartiallyFilled, 
                ExecutedQuantity = 0.2m, 
                Price = 50000m 
            });

        // Leg 2: Fails
        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.2m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed });

        // Undo: Should sell 0.2 BTC on Binance
        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.2m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.5m);

        // Assert
        result.Should().BeFalse();
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.2m), Times.Once);
        _binanceClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.2m), Times.Once); // Undo

        var transactions = _service.GetRecentTransactions();
        transactions.First().Status.Should().Be("Recovered");
        transactions.First().IsRecovered.Should().BeTrue();
    }
}
