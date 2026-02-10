using ArbitrageApi.Configuration;

namespace ArbitrageApi.Services.Stats;

public class DepthThresholdService
{
    private readonly CalendarCache _calendar;
    private readonly PairsConfigRoot _config;

    public DepthThresholdService(CalendarCache calendar, PairsConfigRoot config)
    {
        _calendar = calendar;
        _config = config;
    }

    public double GetDepthThreshold(string pair, DateTime utcNow)
    {
        var zone = _calendar.GetZone(utcNow);
        
        // Find config for this pair, or use default if not found
        // In a real scenario, we might want a fallback "default" config
        var cfg = _config[pair];

        if (cfg == null)
        {
            // Fallback logic if pair is not configured
             // Ensure we return a safe default to prevent crashes, or maybe Log warning
            return 1.0; // Default Optimal
        }

        return zone switch
        {
            ActivityZone.High => cfg.MinDepth,
            ActivityZone.Normal => cfg.OptimalDepth,
            ActivityZone.Low => cfg.AggressiveDepth,
            _ => cfg.OptimalDepth
        };
    }
}
