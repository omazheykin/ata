using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Services.Stats.Processors;

public class HeatmapProcessor : IEventProcessor
{
    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var day = arbitrageEvent.Timestamp.DayOfWeek.ToString();
        var hour = arbitrageEvent.Timestamp.Hour;
        var cellId = $"{day.Substring(0, 3)}-{hour:D2}";

        // Retry logic to handle concurrency conflicts
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Reload the cell from database on each retry to get latest version
                var cell = await dbContext.HeatmapCells.FirstOrDefaultAsync(c => c.Id == cellId, ct);

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
                    };
                    dbContext.HeatmapCells.Add(cell);
                }
                else
                {
                    // Incremental Average: (OldAvg * OldCount + NewVal) / (NewCount)
                    var spreadPercent = arbitrageEvent.Spread * 100;
                    
                    cell.AvgSpread = (cell.AvgSpread * cell.EventCount + spreadPercent) / (cell.EventCount + 1);
                    cell.EventCount++;
                    
                    if (spreadPercent > cell.MaxSpread)
                    {
                        cell.MaxSpread = spreadPercent;
                    }
                }

                await dbContext.SaveChangesAsync(ct);
                return; // Success, exit retry loop
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
            {
                // Detach all tracked entities to avoid conflicts on retry
                foreach (var entry in dbContext.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }
                
                // Wait a bit before retrying (exponential backoff)
                await Task.Delay(10 * (int)Math.Pow(2, attempt), ct);
            }
        }
    }
}
