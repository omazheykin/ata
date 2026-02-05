using System.Text.Json;

namespace ArbitrageApi.Services;

public class AppState
{
    public bool IsSandboxMode { get; set; } = false;
    public bool IsAutoTradeEnabled { get; set; } = false;
    public bool IsAutoRebalanceEnabled { get; set; } = false;
    public decimal MinProfitThreshold { get; set; } = 0.5m;
    public bool IsSmartStrategyEnabled { get; set; } = true;
    public decimal SafeBalanceMultiplier { get; set; } = 0.3m;
    public bool UseTakerFees { get; set; } = true;
    public Dictionary<string, decimal> PairThresholds { get; set; } = new()
    {
        { "BTCUSDT", 0.05m },
        { "ETHUSDT", 0.05m },
        { "SOLUSDT", 0.15m }
    };

    // Phase 4: Safety Kill-Switches
    public decimal MaxDrawdownUsd { get; set; } = 50.0m; // Stop if lost $50
    public int MaxConsecutiveLosses { get; set; } = 3;   // Stop if 3 fails in a row
    public bool IsSafetyKillSwitchTriggered { get; set; } = false;
    public string GlobalKillSwitchReason { get; set; } = string.Empty;

    // PR Feedback Updates
    public decimal MinRebalanceSkewThreshold { get; set; } = 0.1m; // 10% default
}

public class StatePersistenceService
{
    private readonly string _filePath;
    private readonly ILogger<StatePersistenceService> _logger;
    private AppState _currentState;

    public StatePersistenceService(ILogger<StatePersistenceService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appstate.json");
        _currentState = LoadState();
    }

    public virtual AppState GetState() => _currentState;

    public virtual void SaveState(AppState state)
    {
        try
        {
            _currentState = state;
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
            _logger.LogInformation("✅ Application state saved to {FilePath}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save application state");
        }
    }

    private AppState LoadState()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var state = JsonSerializer.Deserialize<AppState>(json);
                if (state != null)
                {
                    _logger.LogInformation("✅ Application state loaded from {FilePath}", _filePath);
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load application state. Using defaults.");
        }

        return new AppState();
    }
}
