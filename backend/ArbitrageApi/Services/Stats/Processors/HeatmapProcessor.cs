using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats.Processors;

public class HeatmapProcessor : IEventProcessor
{
    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        var day = arbitrageEvent.Timestamp.DayOfWeek.ToString();
        var hour = arbitrageEvent.Timestamp.Hour;
        var cellId = $"{day.Substring(0, 3)}-{hour:D2}";

        var cell = await dbContext.HeatmapCells.FirstOrDefaultAsync(c => c.Id == cellId);

        if (cell == null)
        {
            cell = new HeatmapCell
            {
                Id = cellId,
                Day = day.Substring(0, 3),
                Hour = hour,
                EventCount = 1,
                AvgSpread = arbitrageEvent.Spread * 100,
                MaxSpread = arbitrageEvent.Spread * 100,
                DirectionBias = arbitrageEvent.Direction
                // Volatility will be handled by a periodic background job or a simpler rolling logic
            };
            dbContext.HeatmapCells.Add(cell);
        }
        else
        {
            // Incremental Average: (OldAvg * OldCount + NewVal) / (NewCount)
            // Stored values are already percentages (e.g. 0.57), event.Spread is raw (e.g. 0.0057)
            var spreadPercent = arbitrageEvent.Spread * 100;
            
            cell.AvgSpread = (cell.AvgSpread * cell.EventCount + spreadPercent) / (cell.EventCount + 1);
            cell.EventCount++;
            
            if (spreadPercent > cell.MaxSpread)
            {
                cell.MaxSpread = spreadPercent;
            }

            // Direction Bias (Simplistic: keep track of counts)
            // For true bias, we'd need to store counts per direction in the cell.
            // For now, let's just stick to the basic stats to satisfy the modal requirement.
        }

        await dbContext.SaveChangesAsync();
    }
}
