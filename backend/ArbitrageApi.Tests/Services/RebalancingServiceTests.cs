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
    private readonly ITrendAnalysisService _trendService;
    private readonly ChannelProvider _channelProvider;
    private readonly RebalancingService _rebalancingService;

    public RebalancingServiceTests()
    {
        _loggerMock = new Mock<ILogger<RebalancingService>>();
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _trendService = new ManualTrendService();
        _channelProvider = new ChannelProvider();

        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");

        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        _rebalancingService = new RebalancingService(_loggerMock.Object, clients, _trendService, _channelProvider);
    }

    private class ManualTrendService : ITrendAnalysisService
    {
        public Task<AssetTrend> GetTrendAsync(string asset, CancellationToken ct = default) => 
            Task.FromResult(new AssetTrend { Prediction = "Neutral (Manual)" });
        public Task<RebalanceWindow?> GetBestWindowAsync(CancellationToken ct = default) => 
            Task.FromResult<RebalanceWindow?>(null);
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
        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(0.5m);
    }
    
    [Fact]
    public async Task UpdateBalancesAndSkews_HighSkewLowFee_ShouldGenerateViableProposal()
    {
        // Arrange
        var asset = "USDT";
        // Binance: 10,000, Coinbase: 0. Total 10,000. Target 5,000. Move 5,000.
        // Fee: 5. Cost % = 5 / 5000 = 0.1% (< 1% -> Viable)
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 10000m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 0m } });
        
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync(asset)).ReturnsAsync(5.0m);

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        var proposals = _rebalancingService.GetProposals();
        proposals.Should().NotBeEmpty();
        var p = proposals.First(x => x.Asset == asset);
        
        p.IsViable.Should().BeTrue();
        p.CostPercentage.Should().Be(0.1m);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_HighSkewHighFee_ShouldGenerateNonViableProposal()
    {
        // Arrange
        var asset = "USDT";
        // Binance: 100, Coinbase: 0. Total 100. Target 50. Move 50.
        // Fee: 5. Cost % = 5 / 50 = 10% (> 1% -> Not Viable)
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 100m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 0m } });
        
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync(asset)).ReturnsAsync(5.0m);

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        var proposals = _rebalancingService.GetProposals();
        proposals.Should().NotBeEmpty();
        var p = proposals.First(x => x.Asset == asset);
        
        p.IsViable.Should().BeFalse();
        p.CostPercentage.Should().Be(10.0m);
    }
}
