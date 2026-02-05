using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using System.IO.Compression;

namespace ArbitrageApi.Services;

public class ArbitrageExportService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ArbitrageExportService> _logger;

    public ArbitrageExportService(IServiceProvider serviceProvider, ILogger<ArbitrageExportService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<byte[]> ExportCellEventsToZipAsync(string day, int hour)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
        
        var targetDay = ParseDayOfWeek(day);
        
        var events = await dbContext.ArbitrageEvents
            .Where(e => (int)e.Timestamp.DayOfWeek == (int)targetDay && e.Timestamp.Hour == hour)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
        
        // Prepare data for Excel with clean headers
        var excelData = events.Select(e => new {
            Time = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            Pair = e.Pair,
            Direction = e.Direction,
            Spread_Percent = e.SpreadPercent.ToString("F3") + "%",
            Depth_Buy = e.DepthBuy.ToString("F2"),
            Depth_Sell = e.DepthSell.ToString("F2"),
            Event_ID = e.Id
        });

        using var excelStream = new MemoryStream();
        MiniExcel.SaveAs(excelStream, excelData);
        excelStream.Position = 0;

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var fileName = $"Arbitrage_Events_{day}_{hour:D2}-00.xlsx";
            var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            await excelStream.CopyToAsync(entryStream);
        }
        
        return zipStream.ToArray();
    }

    private DayOfWeek ParseDayOfWeek(string day)
    {
        return day.ToUpper() switch
        {
            "MON" => DayOfWeek.Monday,
            "TUE" => DayOfWeek.Tuesday,
            "WED" => DayOfWeek.Wednesday,
            "THU" => DayOfWeek.Thursday,
            "FRI" => DayOfWeek.Friday,
            "SAT" => DayOfWeek.Saturday,
            "SUN" => DayOfWeek.Sunday,
            _ => throw new ArgumentException($"Invalid day: {day}")
        };
    }
}
