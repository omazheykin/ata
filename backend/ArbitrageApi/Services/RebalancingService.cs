using System.Collections.Concurrent;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;

namespace ArbitrageApi.Services;

public class RebalancingService : BackgroundService
{
    private readonly ILogger<RebalancingService> _logger;
    private readonly List<IExchangeClient> _exchangeClients;
    private readonly ConcurrentDictionary<string, decimal> _assetSkews = new(); // -1.0 (heavy Coinbase) to 1.0 (heavy Binance)
    private readonly ConcurrentDictionary<string, Dictionary<string, decimal>> _exchangeBalances = new();

    public RebalancingService(ILogger<RebalancingService> logger, IEnumerable<IExchangeClient> exchangeClients)
    {
        _logger = logger;
        _exchangeClients = exchangeClients.ToList();
    }

    public virtual decimal GetSkew(string asset)
    {
        return _assetSkews.TryGetValue(asset, out var skew) ? skew : 0m;
    }

    public Dictionary<string, decimal> GetAllSkews()
    {
        return new Dictionary<string, decimal>(_assetSkews);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚖️ Rebalancing Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateBalancesAndSkewsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Poll every minute
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating rebalancing skews");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task UpdateBalancesAndSkewsAsync(CancellationToken ct)
    {
        var binance = _exchangeClients.FirstOrDefault(c => c.ExchangeName == "Binance");
        var coinbase = _exchangeClients.FirstOrDefault(c => c.ExchangeName == "Coinbase");

        if (binance == null || coinbase == null) return;

        var binanceBalancesTask = binance.GetBalancesAsync();
        var coinbaseBalancesTask = coinbase.GetBalancesAsync();

        await Task.WhenAll(binanceBalancesTask, coinbaseBalancesTask);

        var binanceBalances = (await binanceBalancesTask).ToDictionary(b => b.Asset, b => b.Free);
        var coinbaseBalances = (await coinbaseBalancesTask).ToDictionary(b => b.Asset, b => b.Free);

        _exchangeBalances["Binance"] = binanceBalances;
        _exchangeBalances["Coinbase"] = coinbaseBalances;

        // Calculate skew for each asset we track
        var assets = binanceBalances.Keys.Union(coinbaseBalances.Keys).Distinct();

        foreach (var asset in assets)
        {
            decimal binanceVal = binanceBalances.TryGetValue(asset, out var b) ? b : 0m;
            decimal coinbaseVal = coinbaseBalances.TryGetValue(asset, out var c) ? c : 0m;
            decimal totalVal = binanceVal + coinbaseVal;

            if (totalVal > 0)
            {
                // Skew = (Binance - Coinbase) / Total
                // 1.0 = All on Binance
                // -1.0 = All on Coinbase
                // 0.0 = Perfectly balanced
                var skew = (binanceVal - coinbaseVal) / totalVal;
                _assetSkews[asset] = Math.Round(skew, 4);
            }
            else
            {
                _assetSkews[asset] = 0m;
            }
        }

        _logger.LogDebug("⚖️ Updated inventory skews for {Count} assets", _assetSkews.Count);
    }
}
