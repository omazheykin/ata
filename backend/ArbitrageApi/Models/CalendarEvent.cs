namespace ArbitrageApi.Models;

public record CalendarEvent
{
    public string Pair { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public double Spread { get; init; }
    public double Depth { get; init; }
    public DateTime TimestampUtc { get; init; }
}
