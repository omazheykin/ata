using ArbitrageApi.Services;
using ArbitrageApi.Models;
using Microsoft.Extensions.Hosting;
using System.Threading;

namespace ArbitrageApi.Services.Strategies;

public class SmartStrategyService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SmartStrategyService> _logger;
    private readonly ArbitrageStatsService _statsService;
    private readonly StatePersistenceService _persistenceService;
    private readonly ChannelProvider _channelProvider;
    private readonly SemaphoreSlim _updateTrigger = new(0, 1);

    public SmartStrategyService(
        IServiceProvider serviceProvider,
        ILogger<SmartStrategyService> logger,
        ArbitrageStatsService statsService,
        StatePersistenceService persistenceService,
        ChannelProvider channelProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _statsService = statsService;
        _persistenceService = persistenceService;
        _channelProvider = channelProvider;
    }

    public void TriggerUpdate()
    {
        try
        {
            if (_updateTrigger.CurrentCount == 0)
                _updateTrigger.Release();
        }
        catch (ObjectDisposedException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Ensure background processing starts
        _logger.LogInformation("🧠 SMART STRATEGY: Loop is now ACTIVE and running.");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var day = now.DayOfWeek.ToString();
                var hour = now.Hour;

                _logger.LogInformation("🧠 SMART STRATEGY: Evaluating market conditions for {Day} {Hour}:00...", day, hour);

                // Use the Stats Service to get current metrics
                var response = await _statsService.GetStatsAsync();
                var currentHourDetail = response.Calendar.GetValueOrDefault(day.Substring(0, 3))?.GetValueOrDefault(hour.ToString("D2"));

                var state = _persistenceService.GetState();
                if (!state.IsSmartStrategyEnabled)
                {
                    _logger.LogInformation("🧠 Strategy Update: Smart Strategy is DISABLED. Skipping update.");
                    await _updateTrigger.WaitAsync(TimeSpan.FromMinutes(15), stoppingToken);
                    continue;
                }

                decimal newThreshold = 0.1m; // Default
                string reason = "Standard market conditions";
                decimal volScore = 0;
                decimal cScore = 0;
                decimal sScore = 0;

                if (currentHourDetail != null)
                {
                    volScore = currentHourDetail.VolatilityScore;
                    
                    if (volScore >= 0.7m)
                    {
                        newThreshold = 0.05m;
                        reason = $"High activity (Score: {volScore:P0}). Opportunities are frequent and spreads are wide, allowing for a lower capture threshold.";
                    }
                    else if (volScore < 0.2m)
                    {
                        newThreshold = 0.15m;
                        reason = $"Quiet market (Score: {volScore:P0}). Low frequency or narrow spreads detected; threshold is raised to avoid unprofitable trades.";
                    }
                    else
                    {
                        reason = $"Balanced conditions (Score: {volScore:P0}). Market activity is moderate; system is using a standard {newThreshold:P1} target.";
                    }
                }
                else
                {
                    reason = $"Initial assessment. Insufficient historical data for {day} {hour}:00; using conservative {newThreshold:P1} base threshold.";
                }

                _logger.LogInformation("🧠 SMART STRATEGY: Pushing new threshold {Threshold}% to Detection Service. Reason: {Reason}", newThreshold, reason);
                
                await _channelProvider.StrategyUpdateChannel.Writer.WriteAsync(new StrategyUpdate
                {
                    MinProfitThreshold = newThreshold,
                    Reason = reason,
                    VolatilityScore = volScore,
                    CountScore = cScore,
                    SpreadScore = sScore
                }, stoppingToken);

                // Wait for next hour or 15 mins for re-evaluation OR manual trigger
                await _updateTrigger.WaitAsync(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Strategy Update Loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
