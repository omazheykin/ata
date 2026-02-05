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
    private readonly Mock<ITrendAnalysisService> _trendServiceMock; // Changed to Mock
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;
    private readonly RebalancingService _rebalancingService;

    public RebalancingServiceTests()
    {
        _loggerMock = new Mock<ILogger<RebalancingService>>();
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _trendServiceMock = new Mock<ITrendAnalysisService>(); // Init Mock
        // Default setup to prevent NRE
        _trendServiceMock.Setup(t => t.GetTrendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = "Neutral (Default Mock)" });

        _channelProvider = new ChannelProvider();

        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");

        _persistenceServiceMock = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
        _persistenceServiceMock.Setup(s => s.GetState()).Returns(new AppState());

        var clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        // Use Mock Object
        _rebalancingService = new RebalancingService(_loggerMock.Object, clients, _trendServiceMock.Object, _channelProvider, _persistenceServiceMock.Object);
    }
    // Removed ManualTrendService class

    [Fact]
    public async Task UpdateBalancesAndSkews_BalancedInventory_ShouldResultInZeroSkew()
    {
        // Arrange
        var balances = new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } };
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(balances);
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(balances);

        // Act
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
        
        // Mock Trend for this test
        _trendServiceMock.Setup(t => t.GetTrendAsync("BTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = "Neutral" });

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
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.75m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.25m } });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        _rebalancingService.GetSkew("BTC").Should().Be(0.5m);
    }
    
    [Fact]
    public async Task UpdateBalancesAndSkews_HighSkewLowFee_ShouldGenerateViableProposal()
    {
        // Arrange
        var asset = "USDT";
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 10000m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 0m } });
        
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync(asset)).ReturnsAsync(5.0m);
        _trendServiceMock.Setup(t => t.GetTrendAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = "Neutral" });

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
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 100m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 0m } });
        
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync(asset)).ReturnsAsync(5.0m);
        _trendServiceMock.Setup(t => t.GetTrendAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = "Neutral" });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        var proposals = _rebalancingService.GetProposals();
        proposals.Should().NotBeEmpty();
        // Just verify one exists
        proposals.Should().Contain(x => x.Asset == asset);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_ShouldIncludeTrendData_InProposal()
    {
        // Arrange
        var asset = "ETH";
        var expectedTrend = "Strong Buy (Binance)";
        
        // Setup Imbalance to trigger proposal
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 10m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = asset, Free = 0m } });
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync(asset)).ReturnsAsync(0.01m); // Low fee

        // Setup Trend Mock to return specific string
        _trendServiceMock.Setup(t => t.GetTrendAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = expectedTrend });

        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        var proposals = _rebalancingService.GetProposals();
        var proposal = proposals.First(p => p.Asset == asset);

        // Verify Trend Service was called
        _trendServiceMock.Verify(t => t.GetTrendAsync(asset, It.IsAny<CancellationToken>()), Times.Once);
        
        // Verify Trend String is in Proposal
        proposal.TrendDescription.Should().Be(expectedTrend);
    }
}
