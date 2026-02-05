namespace ArbitrageApi.Models;

public class ConnectionStatus
{
    public string ExchangeName { get; set; } = string.Empty;
    public string Status { get; set; } = "Disconnected"; // Connected, Connecting, Disconnected, Error
    public int? LatencyMs { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
