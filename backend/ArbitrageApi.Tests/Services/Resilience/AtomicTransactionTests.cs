using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ArbitrageApi.Hubs;
using System.Threading.Channels;

namespace ArbitrageApi.Tests.Services.Resilience;

public class AtomicTransactionTests
{
    private readonly Mock<ILogger<OrderExecutionService>> _loggerMock;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly Mock<IHubContext<ArbitrageHub>> _hubContextMock;
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly OrderExecutionService _service;

    public AtomicTransactionTests()
    {
        _loggerMock = new Mock<ILogger<OrderExecutionService>>();
        
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");
        
        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        
        _hubContextMock = new Mock<IHubContext<ArbitrageHub>>();
        var mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);
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
    public async Task ExecuteTrade_TotalFailure_ShouldRecordFailedTransaction()
    {
        // Arrange
        var opportunity = new ArbitrageOpportunity
        {
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 0.1m
        };

        // Both legs fail
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed, ErrorMessage = "API Down" });

        // Act
        var result = await _service.ExecuteTradeAsync(opportunity, 0.1m);

        // Assert
        result.Should().BeFalse();
        
        // Verify SignalR broadcast (SendAsync is extension method, we verify SendCoreAsync)
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveTransaction", 
            It.Is<object[]>(args => args.Length == 1 && ((Transaction)args[0]).Status == "Failed"), 
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify Channel emission
        _channelProvider.TransactionChannel.Reader.Count.Should().Be(1);
        var tx = await _channelProvider.TransactionChannel.Reader.ReadAsync();
        tx.Status.Should().Be("Failed");
        tx.RealizedProfit.Should().Be(0m);
    }

    [Fact]
    public async Task ExecuteTrade_PartialFill_RecoverySucceeds_ShouldRecordRecoveredStatus()
    {
        // Arrange
        var opportunity = new ArbitrageOpportunity
        {
            Symbol = "BTCUSD",
            BuyExchange = "Binance",
            SellExchange = "Coinbase",
            Volume = 1.0m,
            BuyFee = 0.001m,
            SellFee = 0.0016m
        };

        // 1. Buy 1.0, but only 0.5 filled
        _binanceClientMock.Setup(c => c.PlaceMarketBuyOrderAsync(It.IsAny<string>(), 1.0m))
            .ReturnsAsync(new OrderResponse 
            { 
                Status = OrderStatus.PartiallyFilled, 
                ExecutedQuantity = 0.5m, 
                Price = 40000m,
                OrderId = "B1"
            });

        // 2. Sell 0.5 fails
        _coinbaseClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.5m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Failed });

        // 3. Recovery: Sell 0.5 on Binance succeeds
        _binanceClientMock.Setup(c => c.PlaceMarketSellOrderAsync(It.IsAny<string>(), 0.5m))
            .ReturnsAsync(new OrderResponse { Status = OrderStatus.Filled, OrderId = "B2" });

        // Act
        await _service.ExecuteTradeAsync(opportunity, 0.1m);

        // Assert
        var tx = await _channelProvider.TransactionChannel.Reader.ReadAsync();
        tx.Status.Should().Be("Recovered");
        tx.IsRecovered.Should().BeTrue();
        tx.Amount.Should().Be(0.5m);
        tx.BuyOrderId.Should().Be("B1");
        tx.RecoveryOrderId.Should().Be("B2");
        // Realized profit should be 0 because it's a recovery (even if technically there was a loss/gain on market move, we simplify it as 0 realized for arb stats)
        tx.RealizedProfit.Should().Be(0m);
    }
}
