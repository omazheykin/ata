using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Stats.Processors;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services.Stats;

public class EventProcessorTests
{
    private StatsDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<StatsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StatsDbContext(options);
    }

    [Fact]
    public async Task NormalizationProcessor_CalculatesSpreadPercent_AndNormalizesTimestamp()
    {
        // Arrange
        var processor = new NormalizationProcessor();
        var ev = new ArbitrageEvent
        {
            Spread = 0.0057m, // 0.57%
            Timestamp = DateTime.Now // Local time
        };
        var db = GetInMemoryDbContext();

        // Act
        await processor.ProcessAsync(ev, db, CancellationToken.None);

        // Assert
        Assert.Equal(0.57m, ev.SpreadPercent);
        Assert.Equal(DateTimeKind.Utc, ev.Timestamp.Kind);
    }

    [Fact]
    public async Task PersistenceProcessor_AddsEventToDatabase()
    {
        // Arrange
        var processor = new PersistenceProcessor();
        var db = GetInMemoryDbContext();
        var ev = new ArbitrageEvent { Id = Guid.NewGuid(), Pair = "BTCUSDT" };

        // Act
        await processor.ProcessAsync(ev, db, CancellationToken.None);

        // Assert
        var saved = await db.ArbitrageEvents.FindAsync(ev.Id);
        Assert.NotNull(saved);
        Assert.Equal("BTCUSDT", saved.Pair);
    }

    [Fact]
    public async Task HeatmapProcessor_CreatesNewCell_WhenMissing()
    {
        // Arrange
        var processor = new HeatmapProcessor();
        var db = GetInMemoryDbContext();
        var timestamp = new DateTime(2026, 2, 3, 16, 0, 0, DateTimeKind.Utc); // Tuesday
        var ev = new ArbitrageEvent
        {
            Timestamp = timestamp,
            Spread = 0.01m,
            Direction = "B->O"
        };

        // Act
        await processor.ProcessAsync(ev, db, CancellationToken.None);

        // Assert
        var cell = await db.HeatmapCells.FirstOrDefaultAsync();
        Assert.NotNull(cell);
        Assert.Equal("Tue-16", cell!.Id);
        Assert.Equal(1, cell.EventCount);
        Assert.Equal(1.0m, cell.AvgSpread); // 0.01 * 100
    }

    [Fact]
    public async Task HeatmapProcessor_UpdatesExistingCell_Incrementally()
    {
        // Arrange
        var processor = new HeatmapProcessor();
        var db = GetInMemoryDbContext();
        var timestamp = new DateTime(2026, 2, 3, 16, 0, 0, DateTimeKind.Utc);
        
        // Add initial cell
        db.HeatmapCells.Add(new HeatmapCell 
        { 
            Id = "Tue-16", 
            EventCount = 1, 
            AvgSpread = 1.0m, 
            MaxSpread = 1.0m 
        });
        await db.SaveChangesAsync();

        var ev = new ArbitrageEvent
        {
            Timestamp = timestamp,
            Spread = 0.02m // 2.0%
        };

        // Act
        await processor.ProcessAsync(ev, db, CancellationToken.None);

        // Assert
        var cell = await db.HeatmapCells.FindAsync("Tue-16");
        Assert.NotNull(cell);
        Assert.Equal(2, cell!.EventCount);
        Assert.Equal(1.5m, cell.AvgSpread); // (1.0 + 2.0) / 2
        Assert.Equal(2.0m, cell.MaxSpread);
    }
}
