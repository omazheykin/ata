using System;
using System.Collections.Generic;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Stats;
using Xunit;

namespace ArbitrageApi.Tests.Services.Stats;

public class CalendarCacheTests
{
    [Fact]
    public void AddEvent_ShouldIncrementCountForCorrectHour()
    {
        // Arrange
        var cache = new CalendarCache();
        var now = DateTime.UtcNow;
        var ev = new CalendarEvent { TimestampUtc = now };

        // Act
        cache.AddEvent(ev);
        var zone = cache.GetZone(now);

        // Assert
        // With 1 event, it should be Low
        Assert.Equal(ActivityZone.Low, zone);
    }

    [Fact]
    public void GetZone_ShouldReturnHigh_WhenCountIsLarge()
    {
        // Arrange
        var cache = new CalendarCache();
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        
        // Act
        for (int i = 0; i < 250; i++)
        {
            cache.AddEvent(new CalendarEvent { TimestampUtc = now });
        }
        var zone = cache.GetZone(now);

        // Assert
        Assert.Equal(ActivityZone.High, zone);
    }

    [Fact]
    public void GetZone_ShouldReturnNormal_WhenCountIsMedium()
    {
        // Arrange
        var cache = new CalendarCache();
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        
        // Act
        for (int i = 0; i < 60; i++)
        {
            cache.AddEvent(new CalendarEvent { TimestampUtc = now });
        }
        var zone = cache.GetZone(now);

        // Assert
        Assert.Equal(ActivityZone.Normal, zone);
    }

    [Fact]
    public void Initialize_ShouldSetCorrectCounts()
    {
        // Arrange
        var cache = new CalendarCache();
        var initialCounts = new Dictionary<int, int> { { 10, 300 } };
        var checkTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // Act
        cache.Initialize(initialCounts);
        var zone = cache.GetZone(checkTime);

        // Assert
        Assert.Equal(ActivityZone.High, zone);
    }
}
