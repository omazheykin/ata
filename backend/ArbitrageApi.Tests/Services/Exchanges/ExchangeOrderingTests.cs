using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Services.Exchanges.OKX;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Xunit;

namespace ArbitrageApi.Tests.Services.Exchanges;

public class ExchangeOrderingTests
{
    private readonly Mock<HttpClient> _httpClientMock;
    private readonly Mock<ILogger<BinanceClient>> _binanceLoggerMock;
    private readonly Mock<ILogger<CoinbaseClient>> _coinbaseLoggerMock;
    private readonly Mock<ILogger<OKXClient>> _okxLoggerMock;
    private readonly Mock<IConfiguration> _configurationMock;

    public ExchangeOrderingTests()
    {
        _httpClientMock = new Mock<HttpClient>();
        _binanceLoggerMock = new Mock<ILogger<BinanceClient>>();
        _coinbaseLoggerMock = new Mock<ILogger<CoinbaseClient>>();
        _okxLoggerMock = new Mock<ILogger<OKXClient>>();
        _configurationMock = new Mock<IConfiguration>();

        _configurationMock.Setup(c => c[It.IsAny<string>()]).Returns((string key) => 
            key.EndsWith("Url") ? "https://api.example.com" : "mock_value");
    }

    [Fact]
    public async Task BinanceClient_ShouldImplementOrderMethods()
    {
        // Arrange
        var client = new BinanceClient(new HttpClient(), _binanceLoggerMock.Object, _configurationMock.Object, true);

        // Act & Assert (Should not throw NotImplementedException)
        var response = await client.PlaceMarketBuyOrderAsync("BTCUSDT", 0.01m);
        Assert.NotNull(response);

        var limitResponse = await client.PlaceLimitBuyOrderAsync("BTCUSDT", 0.01m, 50000m);
        Assert.NotNull(limitResponse);

        var status = await client.GetOrderStatusAsync(limitResponse.OrderId);
        Assert.NotNull(status);

        var cancel = await client.CancelOrderAsync(limitResponse.OrderId);
        Assert.True(cancel);
    }

    [Fact]
    public async Task CoinbaseClient_ShouldImplementOrderMethods()
    {
        // Arrange
        var client = new CoinbaseClient(new HttpClient(), _coinbaseLoggerMock.Object, _configurationMock.Object, true);

        // Act & Assert
        var response = await client.PlaceMarketBuyOrderAsync("BTC-USD", 0.01m);
        Assert.NotNull(response);

        var limitResponse = await client.PlaceLimitBuyOrderAsync("BTC-USD", 0.01m, 50000m);
        Assert.NotNull(limitResponse);

        var status = await client.GetOrderStatusAsync(limitResponse.OrderId);
        Assert.NotNull(status);

        var cancel = await client.CancelOrderAsync(limitResponse.OrderId);
        Assert.True(cancel);
    }

    [Fact]
    public async Task OKXClient_ShouldImplementOrderMethods()
    {
        // Arrange
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var client = new OKXClient(_configurationMock.Object, httpClientFactoryMock.Object, loggerFactoryMock.Object, true);

        // Act & Assert
        var response = await client.PlaceMarketBuyOrderAsync("BTC-USDT", 0.01m);
        Assert.NotNull(response);

        var limitResponse = await client.PlaceLimitBuyOrderAsync("BTC-USDT", 0.01m, 50000m);
        Assert.NotNull(limitResponse);

        var status = await client.GetOrderStatusAsync(limitResponse.OrderId);
        Assert.NotNull(status);

        var cancel = await client.CancelOrderAsync(limitResponse.OrderId);
        Assert.True(cancel);
    }
}
