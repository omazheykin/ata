using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class GlobalAggregator : BaseAggregator
{
    protected override (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent)
    {
        return ("Global", "Total");
    }
}
