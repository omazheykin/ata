using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class PassiveRebalancingServiceTests
{
    private readonly Mock<ILogger<PassiveRebalancingService>> _loggerMock;
    private readonly ChannelProvider _channelProvider;
    private readonly Mock<StatePersistenceService> _persistenceServiceMock;
    private readonly Mock<IRebalancingService> _rebalancingServiceMock;
    private readonly Mock<OrderExecutionService> _executionServiceMock;
    private readonly PassiveRebalancingService _service;

    public PassiveRebalancingServiceTests()
    {
        _loggerMock = new Mock<ILogger<PassiveRebalancingService>>();
        _channelProvider = new ChannelProvider(); // Real instance, simpler than mocking channels
        _persistenceServiceMock = new Mock<StatePersistenceService>(
            new Mock<ILogger<StatePersistenceService>>().Object); 

        // Mocking Interface is much safer and cleaner than mocking concrete class
        _rebalancingServiceMock = new Mock<IRebalancingService>();

        _executionServiceMock = new Mock<OrderExecutionService>(
            new Mock<ILogger<OrderExecutionService>>().Object,
            new List<IExchangeClient>(),
            null,
            null);

        _service = new PassiveRebalancingService(
            _loggerMock.Object,
            _channelProvider,
            _persistenceServiceMock.Object,
            _rebalancingServiceMock.Object,
            _executionServiceMock.Object);
    }

    private void SetupState(bool autoTrade = true, bool killSwitch = false, decimal minProfit = 0.5m)
    {
        var state = new AppState
        {
            IsAutoTradeEnabled = autoTrade,
            IsSafetyKillSwitchTriggered = killSwitch,
            MinProfitThreshold = minProfit,
            PairThresholds = new Dictionary<string, decimal>(),
            MinRebalanceSkewThreshold = 0.1m // 10% deviation threshold
        };
        _persistenceServiceMock.Setup(s => s.GetState()).Returns(state);
    }

    [Fact]
    public async Task Execute_When_MovingFromHeavyToLight_ShouldExecute()
    {
        // Arrange
        SetupState();
        
        // Scenario: Binance is Heavy (+0.5), Coinbase is Light (-0.5).
        // Trade: Sell Binance -> Buy Coinbase.
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Binance")).Returns(0.5m);
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Coinbase")).Returns(-0.5m);

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Coinbase", // Target (Light)
            SellExchange = "Binance", // Source (Heavy)
            ProfitPercentage = 0.2m // Low profit, below default 0.5%
        };

        // Act
        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        await Task.Delay(100); 
        await _service.StopAsync(CancellationToken.None);

        // Assert
        // Verify ExecuteTradeAsync was called
        _executionServiceMock.Verify(x => x.ExecuteTradeAsync(
            It.Is<ArbitrageOpportunity>(o => o.Symbol == "BTC-USDT"), 
            It.IsAny<decimal>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ignore_When_MovingFromLightToHeavy_ShouldIgnore()
    {
        // Arrange
        SetupState();
        
        // Scenario: Binance is Heavy (+0.5), Coinbase is Light (-0.5).
        // Bad Trade: Buy Binance (Target +0.5), Sell Coinbase (Source -0.5).
        // Source (-0.5) is NOT > Threshold (0.1).
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Binance")).Returns(0.5m);
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Coinbase")).Returns(-0.5m);

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Binance", // Target (Heavy) -> BAD
            SellExchange = "Coinbase", // Source (Light) -> BAD
            ProfitPercentage = 0.2m 
        };

        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        await Task.Delay(100); 
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _executionServiceMock.Verify(x => x.ExecuteTradeAsync(
            It.IsAny<ArbitrageOpportunity>(), 
            It.IsAny<decimal>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ignore_When_KillSwitchActive_ShouldIgnore()
    {
        // Arrange
        SetupState(killSwitch: true);
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Binance")).Returns(0.5m);
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Coinbase")).Returns(-0.5m);

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Coinbase",
            SellExchange = "Binance",
            ProfitPercentage = 0.4m 
        };

        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        await Task.Delay(100); 
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _executionServiceMock.Verify(x => x.ExecuteTradeAsync(
            It.IsAny<ArbitrageOpportunity>(), 
            It.IsAny<decimal>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ignore_When_ProfitIsTooLow_EvenForRebalance()
    {
        // Arrange
        SetupState();
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Binance")).Returns(0.5m);
        _rebalancingServiceMock.Setup(r => r.GetDeviation("BTC", "Coinbase")).Returns(-0.5m);

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Coinbase",
            SellExchange = "Binance", 
            ProfitPercentage = 0.001m // < 0.01% Absolute Min
        };

        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        await Task.Delay(100);
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _executionServiceMock.Verify(x => x.ExecuteTradeAsync(
            It.IsAny<ArbitrageOpportunity>(), 
            It.IsAny<decimal>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
