using ArbitrageApi.Models;
using FluentAssertions;
using Xunit;

namespace ArbitrageApi.Tests.Models;

public class OrderModelsTests
{
    [Fact]
    public void OrderResponse_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var orderResponse = new OrderResponse();

        // Assert
        orderResponse.OrderId.Should().BeEmpty();
        orderResponse.Symbol.Should().BeEmpty();
        orderResponse.Status.Should().Be(OrderStatus.Pending);
        orderResponse.OriginalQuantity.Should().Be(0);
        orderResponse.ExecutedQuantity.Should().Be(0);
    }

    [Fact]
    public void OrderInfo_RemainingQuantity_ShouldCalculateCorrectly()
    {
        // Arrange
        var orderInfo = new OrderInfo
        {
            OriginalQuantity = 1.0m,
            ExecutedQuantity = 0.6m
        };

        // Act
        var remaining = orderInfo.RemainingQuantity;

        // Assert
        remaining.Should().Be(0.4m);
    }

    [Theory]
    [InlineData(OrderType.Market, OrderSide.Buy)]
    [InlineData(OrderType.Market, OrderSide.Sell)]
    [InlineData(OrderType.Limit, OrderSide.Buy)]
    [InlineData(OrderType.Limit, OrderSide.Sell)]
    public void OrderRequest_ShouldAcceptAllValidCombinations(OrderType type, OrderSide side)
    {
        // Arrange & Act
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Type = type,
            Side = side,
            Quantity = 0.001m,
            Price = type == OrderType.Limit ? 50000m : null
        };

        // Assert
        request.Symbol.Should().Be("BTCUSDT");
        request.Type.Should().Be(type);
        request.Side.Should().Be(side);
        request.Quantity.Should().Be(0.001m);
        
        if (type == OrderType.Limit)
        {
            request.Price.Should().Be(50000m);
        }
    }

    [Fact]
    public void OrderStatus_ShouldHaveAllExpectedValues()
    {
        // Assert - Verify all enum values exist
        var statuses = Enum.GetValues<OrderStatus>();
        
        statuses.Should().Contain(OrderStatus.Pending);
        statuses.Should().Contain(OrderStatus.PartiallyFilled);
        statuses.Should().Contain(OrderStatus.Filled);
        statuses.Should().Contain(OrderStatus.Cancelled);
        statuses.Should().Contain(OrderStatus.Failed);
        statuses.Should().Contain(OrderStatus.Rejected);
    }
}
