using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class DayAggregator : BaseAggregator
{
    protected override (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent)
    {
        return ("Day", arbitrageEvent.Timestamp.DayOfWeek.ToString());
    }
}
