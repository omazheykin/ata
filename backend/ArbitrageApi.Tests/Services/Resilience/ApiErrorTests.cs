using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ArbitrageApi.Hubs;

namespace ArbitrageApi.Tests.Services.Resilience;

public class ApiErrorTests
{
    private readonly Mock<ILogger<OrderExecutionService>> _loggerMock;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly Mock<IHubContext<ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly OrderExecutionService _service;

    public ApiErrorTests()
    {
        _loggerMock = new Mock<ILogger<OrderExecutionService>>();
        
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");
        
        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        
        _hubContextMock = new Mock<IHubContext<ArbitrageHub>>();
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
    public async Task ExecuteSequential_Leg2ServiceUnavailable_ShouldTriggerRecovery()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Sequential);
        var opportunity = new ArbitrageOpportunity
        {
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 1.0m
        };

        // Leg 1: Success
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 1.0m, OrderId = "B1" });

        // Leg 2: 503 Service Unavailable / Failed
        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed, ErrorMessage = "503 Service Unavailable" });

        // Recovery: Sell back on Leg 1 exchange
        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, OrderId = "B2" });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.1m);

        // Assert
        result.Should().BeFalse();
        _coinbaseClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 1.0m), Times.Once);
        _binanceClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 1.0m), Times.Once); // Recovery triggered
        
        var transactions = _service.GetRecentTransactions();
        transactions.Should().NotBeEmpty();
        transactions.First().Status.Should().Be("Recovered");
        transactions.First().IsRecovered.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteConcurrent_OneLegRateLimited_ShouldTriggerRecoveryOnOtherLeg()
    {
        // Arrange
        _service.SetExecutionStrategy(ExecutionStrategy.Concurrent);
        var opportunity = new ArbitrageOpportunity
        {
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.5m
        };

        // Binance succeeds, Coinbase fails with Rate Limit
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), 0.5m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, ExecutedQuantity = 0.5m, OrderId = "B1" });
        
        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.5m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed, ErrorMessage = "429 Too Many Requests" });

        // Recovery: Sell back on Binance
        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.5m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, OrderId = "B2" });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.1m);

        // Assert
        result.Should().BeFalse();
        _binanceClientMock.Verify(c => c.PlaceMarketSellOrderAsync("BTCUSD", 0.5m), Times.Once);
        
        var transactions = _service.GetRecentTransactions();
        transactions.First().Status.Should().Be("Recovered");
    }
}
