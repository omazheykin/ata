using System.Collections.Concurrent;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public enum ActivityZone
{
    Low,
    Normal,
    High
}

// Moved to top

public class CalendarCache
{
    // Thread-safe dictionary: Hour (0-23) -> Count
    private readonly ConcurrentDictionary<int, int> _hourCounts = new();

    public void AddEvent(CalendarEvent ev)
    {
        var hour = ev.TimestampUtc.Hour;
        if (hour >= 0 && hour < 24)
        {
            // Atomically increment the count for the hour
            _hourCounts.AddOrUpdate(hour, 1, (key, oldValue) => oldValue + 1);
        }
    }

    public ActivityZone GetZone(DateTime utcNow)
    {
        var hour = utcNow.Hour;
        
        // Get count or default to 0 if not present
        if (!_hourCounts.TryGetValue(hour, out int count))
        {
            count = 0;
        }

        if (count >= 200)
            return ActivityZone.High;

        if (count >= 50)
            return ActivityZone.Normal;

        return ActivityZone.Low;
    }
    
    // Optional: Method to reset counts daily or load from DB on startup
    public void Initialize(Dictionary<int, int> initialCounts)
    {
        // For initialization, we can clear and repopulate
        _hourCounts.Clear();
        foreach (var (hour, count) in initialCounts)
        {
            if (hour >= 0 && hour < 24)
            {
                _hourCounts[hour] = count;
            }
        }
    }
}
