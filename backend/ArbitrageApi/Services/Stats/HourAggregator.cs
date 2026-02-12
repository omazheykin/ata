using System.Globalization;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Stats;

public class HourAggregator : BaseAggregator
{
    protected override (string Category, string Key) GetMetricKey(ArbitrageEvent arbitrageEvent)
    {
        var timestamp = arbitrageEvent.Timestamp;
        var dayShort = timestamp.ToString("ddd", CultureInfo.InvariantCulture);
        var hour = timestamp.Hour;
        return ("Hour", $"{dayShort}-{hour:D2}");
    }
}
