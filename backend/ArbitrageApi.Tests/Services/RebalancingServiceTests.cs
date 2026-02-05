using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class RebalancingServiceTests
{
    private readonly Mock<ILogger<RebalancingService>> _loggerMock;
    private readonly Mock<IExchangeClient> _binanceClientMock;
    private readonly Mock<IExchangeClient> _coinbaseClientMock;
    private readonly RebalancingService _rebalancingService;

    public RebalancingServiceTests()
    {
        _loggerMock = new Mock<ILogger<RebalancingService>>();
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();

        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");

        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        _rebalancingService = new RebalancingService(_loggerMock.Object, clients);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_BalancedInventory_ShouldResultInZeroSkew()
    {
        // Arrange
        var balances = new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } };
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(balances);
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(balances);

        // Act
        // Use reflection to call private method for testing logic without waiting for background loop
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(0m);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_AllOnBinance_ShouldResultInOneSkew()
    {
        // Arrange
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.0m } });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(1.0m);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_AllOnCoinbase_ShouldResultInNegativeOneSkew()
    {
        // Arrange
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.0m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(-1.0m);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_PartialSymmetry_ShouldCalculateCorrectSkew()
    {
        // Arrange
        // Binance: 0.75, Coinbase: 0.25. Total: 1.0. Skew: (0.75 - 0.25) / 1.0 = 0.5
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.75m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.25m } });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(0.5m);
    }
}
