using ArbitrageApi.Models;
using ArbitrageApi.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services;

public class ArbitrageCalculatorTests
{
    private readonly ArbitrageCalculator _calculator;

    public ArbitrageCalculatorTests()
    {
        _calculator = new ArbitrageCalculator(new Mock<Microsoft.Extensions.Logging.ILogger<ArbitrageCalculator>>().Object);
    }

    [Fact]
    public void CalculateOpportunity_NoOrderBooks_ShouldReturnNull()
    {
        // Act
        var result = _calculator.CalculateOpportunity(
            "BTCUSD", 
            new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>(),
            new Dictionary<string, (decimal Maker, decimal Taker)>(),
            true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateOpportunity_SingleExchange_ShouldReturnNull()
    {
        // Arrange
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)> { (50000m, 1.0m) }, new List<(decimal, decimal)> { (50100m, 1.0m) }) }
        };
        var fees = new Dictionary<string, (decimal, decimal)>
        {
            { "Binance", (0.001m, 0.001m) }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateOpportunity_ProfitableOpportunity_ShouldReturnOpportunity()
    {
        // Arrange
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)> { (49000m, 1.0m) }, new List<(decimal, decimal)> { (49500m, 1.0m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (51000m, 1.0m) }, new List<(decimal, decimal)> { (51500m, 1.0m) }) }
        };
        var fees = new Dictionary<string, (decimal, decimal)>
        {
            { "Binance", (0.001m, 0.001m) },
            { "Coinbase", (0.001m, 0.001m) }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true);

        // Assert
        result.Should().NotBeNull();
        result!.BuyExchange.Should().Be("Binance");
        result.SellExchange.Should().Be("Coinbase");
        result.ProfitPercentage.Should().BeGreaterThan(0);
        result.Volume.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateOpportunity_WalkingTheBook_ShouldCalculateCorrectAveragePrice()
    {
        // Arrange
        // We want to buy 1.0 BTC. 
        // Binance has 0.5 at 50000 and 0.5 at 51000. Avg buy = 50500.
        // Coinbase has 1.0 at 52000. Avg sell = 52000.
        // Gross profit = (52000 - 50500) / 50500 = 2.97%
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)>(), new List<(decimal, decimal)> { (50000m, 0.5m), (51000m, 0.5m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (52000m, 1.0m) }, new List<(decimal, decimal)>()) }
        };
        var fees = new Dictionary<string, (decimal, decimal)>
        {
            { "Binance", (0m, 0m) },
            { "Coinbase", (0m, 0m) }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true);

        // Assert
        result.Should().NotBeNull();
        result!.BuyPrice.Should().Be(50500m);
        result.SellPrice.Should().Be(52000m);
    }

    [Fact]
    public void CalculateOpportunity_InsufficientVolume_ShouldLimitToAvailable()
    {
         // Arrange
        // Ask 1.0 BTC total, but only 0.1 available on one side.
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)>(), new List<(decimal, decimal)> { (50000m, 0.1m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (51000m, 1.0m) }, new List<(decimal, decimal)>()) }
        };
        var fees = new Dictionary<string, (decimal, decimal)>
        {
            { "Binance", (0m, 0m) },
            { "Coinbase", (0m, 0m) }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true);

        // Assert
        result.Should().NotBeNull();
        result!.Volume.Should().Be(0.1m);
    }

    [Fact]
    public void CalculateOpportunity_ShouldUseTakerFees_ByDefault()
    {
        // Arrange
        // Gross profit = (50500 - 50000) / 50000 = 1.0%
        // Maker Fee = 0.1%, Taker Fee = 0.2%
        // Net Profit (Taker) = 1.0% - 0.2% - 0.2% = 0.6%
        // Net Profit (Maker) = 1.0% - 0.1% - 0.1% = 0.8%
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)>(), new List<(decimal, decimal)> { (50000m, 1.0m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (50500m, 1.0m) }, new List<(decimal, decimal)>()) }
        };
        var fees = new Dictionary<string, (decimal Maker, decimal Taker)>
        {
            { "Binance", (0.001m, 0.002m) },
            { "Coinbase", (0.001m, 0.002m) }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true);

        // Assert
        result.Should().NotBeNull();
        result!.ProfitPercentage.Should().Be(0.6m); // Confirms Taker fees (0.2% * 2) were subtracted
        result.BuyFee.Should().Be(0.002m);
    }

    [Fact]
    public void CalculateOpportunity_PairThreshold_ShouldOverrideDefault()
    {
        // Arrange
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)>(), new List<(decimal, decimal)> { (50000m, 1.0m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (50200m, 1.0m) }, new List<(decimal, decimal)>()) }
        };
        var fees = new Dictionary<string, (decimal Maker, decimal Taker)>
        {
            { "Binance", (0m, 0m) },
            { "Coinbase", (0m, 0m) }
        };
        
        // Gross/Net profit is 0.4%
        // Default threshold is 0.1% (should pass)
        // Pair threshold for BTCUSD is 0.5% (should fail)
        var pairThresholds = new Dictionary<string, decimal> { { "BTCUSD", 0.5m } };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSD", orderBooks, fees, true, minProfitThreshold: 0.1m, pairThresholds: pairThresholds);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateOpportunity_SafeBalanceMultiplier_ShouldLimitVolume()
    {
        // Arrange
        var orderBooks = new Dictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>
        {
            { "Binance", (new List<(decimal, decimal)>(), new List<(decimal, decimal)> { (50000m, 10.0m) }) },
            { "Coinbase", (new List<(decimal, decimal)> { (51000m, 10.0m) }, new List<(decimal, decimal)>()) }
        };
        var fees = new Dictionary<string, (decimal Maker, decimal Taker)>
        {
            { "Binance", (0m, 0m) },
            { "Coinbase", (0m, 0m) }
        };

        // Balances: $10,000 USDT on Binance. 
        // With 10% multiplier, we can only use $1,000 for buying.
        // At 50,000 price, $1,000 / 50,000 = 0.02 BTC.
        var balances = new Dictionary<string, List<Balance>>
        {
            { "Binance", new List<Balance> { new Balance { Asset = "USDT", Free = 10000m } } },
            { "Coinbase", new List<Balance> { new Balance { Asset = "BTC", Free = 100m } } }
        };

        // Act
        var result = _calculator.CalculateOpportunity("BTCUSDT", orderBooks, fees, true, balances: balances, safeBalanceMultiplier: 0.1m);

        // Assert
        result.Should().NotBeNull();
        result!.Volume.Should().Be(0.02m);
    }

    [Fact]
    public void CalculatePairOpportunity_ShouldRespectIndividualBalanceLists()
    {
        // Arrange
        var asks = new List<(decimal Price, decimal Quantity)> { (50000m, 10.0m) };
        var bids = new List<(decimal Price, decimal Quantity)> { (51000m, 10.0m) };
        var buyFees = (0.001m, 0.001m);
        var sellFees = (0.001m, 0.001m);
        
        var buyBalances = new List<Balance> { new Balance { Asset = "USDT", Free = 10000m } };
        var sellBalances = new List<Balance> { new Balance { Asset = "BTC", Free = 100m } };

        // Act
        // At 50,000 price, $10,000 USDT can buy 0.2 BTC.
        // With 0.3 multiplier (default), we can use $3,000 USDT which buys 0.06 BTC.
        // On sell side, 100 BTC * 0.3 = 30 BTC.
        // Min of liquidity(10), buy(0.06), sell(30) is 0.06.
        var result = _calculator.CalculatePairOpportunity(
            "BTCUSDT", "Binance", "Coinbase", 
            asks, bids, buyFees, sellFees, true, 
            buyBalances: buyBalances, sellBalances: sellBalances);

        // Assert
        result.Should().NotBeNull();
        result!.Volume.Should().Be(0.06m);
    }
}
