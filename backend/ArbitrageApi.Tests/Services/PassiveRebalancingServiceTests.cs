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
            PairThresholds = new Dictionary<string, decimal>()
        };
        _persistenceServiceMock.Setup(s => s.GetState()).Returns(state);
    }

    [Fact]
    public async Task Execute_When_SkewPositive_And_SellingOnBinance_ShouldExecute()
    {
        // Arrange
        SetupState();
        
        // Positive Skew = Heavy on Binance. We want to SELL on Binance.
        _rebalancingServiceMock.Setup(r => r.GetSkew("BTC")).Returns(0.8m); 

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Coinbase",
            SellExchange = "Binance", // Selling on Binance reduces the heavy bag -> GOOD
            ProfitPercentage = 0.2m // Low profit, below default 0.5%
        };

        // Act
        // We write to channel and wait a bit for background service to process
        // But since we can't easily wait for "processed", we might extract the processing method logic 
        // OR we can rely on verifying the mock call.
        // To test robustly without waiting for background thread race conditions, 
        // we can invoke the private ProcessOpportunityAsync via reflection or just trust the loop 
        // if we run the service briefly.
        // BETTER: Refactor service to have public Process method? 
        // For now, let's start the service, write, and wait.
        
        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        // Allow some time for processing
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
    public async Task Ignore_When_SkewPositive_And_BuyingOnBinance_ShouldIgnore()
    {
        // Arrange
        SetupState();
        
        // Positive Skew = Heavy on Binance. 
        // Buying on Binance (Sell Coinbase) -> Makes skew WORSE (Heavy -> Heaviers)
        _rebalancingServiceMock.Setup(r => r.GetSkew("BTC")).Returns(0.8m); 

        var chance = new ArbitrageOpportunity
        {
            Symbol = "BTC-USDT",
            Asset = "BTC",
            BuyExchange = "Binance", // Buying on Binance increases skew -> BAD
            SellExchange = "Coinbase", 
            ProfitPercentage = 0.2m 
        };

        using var cts = new CancellationTokenSource(5000);
        var task = _service.StartAsync(cts.Token);
        
        await _channelProvider.PassiveRebalanceChannel.Writer.WriteAsync(chance);
        
        await Task.Delay(100); 
        await _service.StopAsync(CancellationToken.None);

        // Assert
        // Should NOT execute
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
        _rebalancingServiceMock.Setup(r => r.GetSkew("BTC")).Returns(0.8m); 

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
        _rebalancingServiceMock.Setup(r => r.GetSkew("BTC")).Returns(0.8m); 

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
