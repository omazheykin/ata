using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ArbitrageApi.Data;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ArbitrageApi.Tests.Services
{
    public class ArbitrageExportServiceTests
    {
        private readonly Mock<ILogger<ArbitrageExportService>> _mockLogger;
        private readonly IServiceProvider _serviceProvider;

        public ArbitrageExportServiceTests()
        {
            _mockLogger = new Mock<ILogger<ArbitrageExportService>>();
            
            var services = new ServiceCollection();
            services.AddDbContext<StatsDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            _serviceProvider = services.BuildServiceProvider();
        }

        private async Task SeedDataAsync(StatsDbContext dbContext)
        {
            var now = DateTime.UtcNow;
            var targetTime = new DateTime(now.Year, now.Month, now.Day, 14, 30, 0, DateTimeKind.Utc);
            
            // Tuesday, Feb 3, 2026 (for example)
            var events = new List<ArbitrageEvent>
            {
                new ArbitrageEvent 
                { 
                    Id = Guid.NewGuid(), 
                    Pair = "ETHUSDT", 
                    Direction = "B->C", 
                    Spread = 0.005m, 
                    SpreadPercent = 0.5m,
                    DepthBuy = 1000m, 
                    DepthSell = 2000m, 
                    Timestamp = targetTime 
                },
                new ArbitrageEvent 
                { 
                    Id = Guid.NewGuid(), 
                    Pair = "BTCUSDT", 
                    Direction = "C->B", 
                    Spread = 0.008m, 
                    SpreadPercent = 0.8m,
                    DepthBuy = 500m, 
                    DepthSell = 1500m, 
                    Timestamp = targetTime.AddMinutes(5) 
                }
            };

            dbContext.ArbitrageEvents.AddRange(events);
            await dbContext.SaveChangesAsync();
        }

        [Fact]
        public async Task ExportCellEventsToZipAsync_ReturnsValidZipArchive()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
            await SeedDataAsync(dbContext);
            
            var service = new ArbitrageExportService(_serviceProvider, _mockLogger.Object);
            
            // Act
            var now = DateTime.UtcNow;
            var dayStr = now.DayOfWeek.ToString().Substring(0, 3).ToUpper(); // e.g. "TUE"
            var result = await service.ExportCellEventsToZipAsync(dayStr, 14);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Verify it's a valid ZIP
            using var ms = new MemoryStream(result);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            
            Assert.Single(archive.Entries);
            var entry = archive.Entries[0];
            Assert.StartsWith($"Arbitrage_Events_{dayStr}_14-00", entry.Name);
            Assert.EndsWith(".xlsx", entry.Name);
            Assert.True(entry.Length > 0);
        }

        [Fact]
        public async Task ExportCellEventsToZipAsync_ReturnsEmptyZip_WhenNoEventsFound()
        {
            // Arrange
            var service = new ArbitrageExportService(_serviceProvider, _mockLogger.Object);
            
            // Act
            var result = await service.ExportCellEventsToZipAsync("FRI", 3); // Random time with no data

            // Assert
            Assert.NotNull(result);
            
            using var ms = new MemoryStream(result);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            Assert.Single(archive.Entries); // Should still create a file, just empty or with headers only
        }
    }
}
