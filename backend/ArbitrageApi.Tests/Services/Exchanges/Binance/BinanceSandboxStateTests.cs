using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services.Exchanges.Binance;

public class BinanceSandboxStateTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly BinanceSandboxState _sandboxState;
    private readonly HttpClient _httpClient;

    public BinanceSandboxStateTests()
    {
        _loggerMock = new Mock<ILogger>();
        
        var handler = MockHttpMessageHandler.CreateWithJsonResponse(new { price = "50000.00" });
        _httpClient = new HttpClient(handler);
        
        _sandboxState = new BinanceSandboxState(
            _httpClient,
            _loggerMock.Object,
            "test-api-key",
            "test-api-secret"
        );
    }

    [Fact]
    public async Task PlaceMarketBuyOrderAsync_ShouldReturnFilledOrderAndUpdateBalances()
    {
        // Arrange
        var symbol = "BTCUSDT";
        var quantity = 0.001m;
        var initialBalances = await _sandboxState.GetBalancesAsync();
        var initialUsd = initialBalances.First(b => b.Asset == "USD").Free;

        // Act
        var response = await _sandboxState.PlaceMarketBuyOrderAsync(symbol, quantity);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(OrderStatus.Filled);
        
        var finalBalances = await _sandboxState.GetBalancesAsync();
        var finalUsd = finalBalances.First(b => b.Asset == "USD").Free;
        var finalBtc = finalBalances.First(b => b.Asset == "BTC").Free;

        finalUsd.Should().BeLessThan(initialUsd);
        finalBtc.Should().BeGreaterThan(0.5m); // Initial was 0.5
    }

    [Fact]
    public async Task DepositSandboxFundsAsync_ShouldIncreaseBalance()
    {
        // Arrange
        var asset = "DOGE";
        var amount = 1000m;

        // Act
        await _sandboxState.DepositSandboxFundsAsync(asset, amount);
        var balances = await _sandboxState.GetBalancesAsync();

        // Assert
        balances.Should().Contain(b => b.Asset == asset && b.Free == amount);
    }

    [Fact]
    public async Task GetOrderStatusAsync_ShouldReturnFilledStatus()
    {
        // Arrange
        var orderId = "SANDBOX_test123";

        // Act
        var orderInfo = await _sandboxState.GetOrderStatusAsync(orderId);

        // Assert
        orderInfo.Should().NotBeNull();
        orderInfo.OrderId.Should().Be(orderId);
        orderInfo.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task GetBalancesAsync_ShouldReturnDefaultInitialBalances()
    {
        // Act
        var balances = await _sandboxState.GetBalancesAsync();

        // Assert
        balances.Should().Contain(b => b.Asset == "USD" && b.Free == 10000m);
        balances.Should().Contain(b => b.Asset == "BTC" && b.Free == 0.5m);
    }
}
