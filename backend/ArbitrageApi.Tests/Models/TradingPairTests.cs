using ArbitrageApi.Models;
using Xunit;

namespace ArbitrageApi.Tests.Models;

public class TradingPairTests
{
    [Fact]
    public void CommonPairs_ShouldContainSonicAndNotFantom()
    {
        // Assert
        Assert.Contains(TradingPair.CommonPairs, p => p.BaseAsset == "S");
        Assert.DoesNotContain(TradingPair.CommonPairs, p => p.BaseAsset == "FTM");
    }

    [Theory]
    [InlineData("BTC", "USDT", "BTCUSDT", "BTC-USDT", "BTC-USD")]
    [InlineData("S", "USDT", "SUSDT", "S-USDT", "S-USD")]
    public void GetSymbols_ShouldReturnCorrectFormats(string @base, string quote, string expectedBinance, string expectedOKX, string expectedCoinbase)
    {
        // Arrange
        var pair = new TradingPair(@base, quote);

        // Assert
        Assert.Equal(expectedBinance, pair.Symbol);
        Assert.Equal(expectedOKX, pair.GetOKXSymbol());
        Assert.Equal(expectedCoinbase, pair.GetCoinbaseSymbol());
    }
}
