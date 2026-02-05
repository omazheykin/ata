using ArbitrageApi.Hubs;
using Microsoft.AspNetCore.SignalR;
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
    private readonly Mock<IExchangeClient> _okxClientMock; // Added 3rd exchange
    private readonly Mock<ITrendAnalysisService> _trendServiceMock; // Merged Mock
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;
    private readonly Mock<IHubContext<ArbitrageHub>> _hubContextMock;
    private readonly RebalancingService _rebalancingService;
    private readonly List<IExchangeClient> _clients;

    public RebalancingServiceTests()
    {
        _loggerMock = new Mock<ILogger<RebalancingService>>();
        _binanceClientMock = new Mock<IExchangeClient>();
        _coinbaseClientMock = new Mock<IExchangeClient>();
        _okxClientMock = new Mock<IExchangeClient>();
        _trendServiceMock = new Mock<ITrendAnalysisService>();
        
        // Default setup to prevent NRE for Trend Service
        _trendServiceMock.Setup(t => t.GetTrendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetTrend { Prediction = "Neutral (Default Mock)" });
            
        _channelProvider = new ChannelProvider();
        _hubContextMock = new Mock<IHubContext<ArbitrageHub>>();

        _binanceClientMock.Setup(c => c.ExchangeName).Returns("Binance");
        _coinbaseClientMock.Setup(c => c.ExchangeName).Returns("Coinbase");
        _okxClientMock.Setup(c => c.ExchangeName).Returns("OKX");

        _persistenceServiceMock = new Mock<StatePersistenceService>(new Mock<ILogger<StatePersistenceService>>().Object);
        _persistenceServiceMock.Setup(s => s.GetState()).Returns(new AppState());

        // Default to just 2 for basic tests, override in specific tests if needed
        _clients = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object };
        
        _rebalancingService = new RebalancingService(_loggerMock.Object, _clients, _trendServiceMock.Object, _channelProvider, _persistenceServiceMock.Object, _hubContextMock.Object);
        
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    private async Task InvokeUpdateMethod(RebalancingService service)
    {
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;
    }

    [Fact]
    public async Task TwoExchanges_PerfectBalance_ShouldHaveZeroDeviation()
    {
        // Arrange
        // Total 2.0. Mean 1.0. Both have 1.0. Deviation = 1.0 - 1.0 = 0.
        // Normalized Deviation = 0 / 2.0 = 0.
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } });
        
        // Act
        var method = typeof(RebalancingService).GetMethod("UpdateBalancesAndSkewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(_rebalancingService, new object[] { CancellationToken.None })!;

        // Assert
        // Mean = 1.0. Binance (1.0) -> Dev 0. Coinbase (1.0) -> Dev 0.
        _rebalancingService.GetDeviation("BTC", "Binance").Should().Be(0m);
    }
    
    [Fact]
    public async Task TwoExchanges_AllOnBinance_ShouldHaveMaxDeviation()
    {
        // Arrange
        // Total 2.0. Mean 1.0. 
        // Binance: 2.0. Dev = 1.0. Norm = 1.0 / 2.0 = 0.5.
        // Coinbase: 0.0. Dev = -1.0. Norm = -1.0 / 2.0 = -0.5.
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 2.0m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.0m } });

        // Act
        await InvokeUpdateMethod(_rebalancingService);

        // Assert
        _rebalancingService.GetDeviation("BTC", "Binance").Should().Be(0.5m);
        _rebalancingService.GetDeviation("BTC", "Coinbase").Should().Be(-0.5m);
    }

    [Fact]
    public async Task UpdateBalancesAndSkews_AllOnCoinbase_ShouldResultInNegativeOneSkew()
    {
        // Arrange
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 0.0m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "BTC", Free = 1.0m } });

        // Act
        await InvokeUpdateMethod(_rebalancingService);

        // Assert
        // Total 1.0. Mean 0.5.
        // Coinbase: 1.0. Dev 0.5. Norm = 0.5/1.0 = 0.5. (Positive relative to mean, but negative direction?)
        // Wait, logic is: Deviation = (Val - Mean) / Total.
        // Binance (0) - 0.5 = -0.5. Norm = -0.5.
        // Coinbase (1) - 0.5 = 0.5. Norm = 0.5.
        _rebalancingService.GetDeviation("BTC", "Binance").Should().Be(-0.5m);
        _rebalancingService.GetDeviation("BTC", "Coinbase").Should().Be(0.5m);
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
        await InvokeUpdateMethod(_rebalancingService);

        // Assert
        var proposals = _rebalancingService.GetProposals();
        proposals.Should().NotBeEmpty();
        var p = proposals.First(x => x.Asset == asset);
        
        p.IsViable.Should().BeTrue();
        p.CostPercentage.Should().Be(0.1m);
    }

    [Fact]
    public async Task ThreeExchanges_MixedBalance_ShouldCalculateCorrectDeviations()
    {
        // Arrange
        var clients3 = new List<IExchangeClient> { _binanceClientMock.Object, _coinbaseClientMock.Object, _okxClientMock.Object };
        var service3 = new RebalancingService(_loggerMock.Object, clients3, _trendServiceMock.Object, _channelProvider, _persistenceServiceMock.Object, _hubContextMock.Object);

        // Scenario:
        // Binance: 150
        // Coinbase: 100
        // OKX: 50
        // Total: 300. Mean: 100.
        // Binance: Dev +50. Norm = 50 / 300 = 0.1666...
        // Coinbase: Dev 0. Norm 0.
        // OKX: Dev -50. Norm = -50 / 300 = -0.1666...
        
        _binanceClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "USDT", Free = 150m } });
        _coinbaseClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "USDT", Free = 100m } });
        _okxClientMock.Setup(c => c.GetBalancesAsync()).ReturnsAsync(new List<Balance> { new Balance { Asset = "USDT", Free = 50m } });
        
        _binanceClientMock.Setup(c => c.GetWithdrawalFeeAsync("USDT")).ReturnsAsync(1m);

        // Act
        await InvokeUpdateMethod(service3);

        // Assert
        decimal binanceDev = service3.GetDeviation("USDT", "Binance");
        decimal coinbaseDev = service3.GetDeviation("USDT", "Coinbase");
        decimal okxDev = service3.GetDeviation("USDT", "OKX");

        binanceDev.Should().BeApproximately(0.1667m, 0.001m);
        coinbaseDev.Should().Be(0m);
        okxDev.Should().BeApproximately(-0.1667m, 0.001m);
        
        // Proposal Check
        var proposals = service3.GetProposals();
        proposals.Should().HaveCount(1);
        var p = proposals.First();
        
        // Should move from Heaviest (Binance 150) to Lightest (OKX 50)
        // Amount = (150 - 50) / 2 = 50.
        p.Direction.Should().Contain("Binance");
        p.Direction.Should().Contain("OKX");
        p.Amount.Should().Be(50m);
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
