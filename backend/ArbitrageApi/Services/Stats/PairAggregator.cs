using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class PairAggregator : BaseAggregator
{
    protected override (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent)
    {
        return ("Pair", arbitrageEvent.Pair);
    }
}
