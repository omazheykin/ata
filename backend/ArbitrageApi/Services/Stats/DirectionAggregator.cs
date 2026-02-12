using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class DirectionAggregator : BaseAggregator
{
    protected override (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent)
    {
        return ("Direction", arbitrageEvent.Direction);
    }
}
