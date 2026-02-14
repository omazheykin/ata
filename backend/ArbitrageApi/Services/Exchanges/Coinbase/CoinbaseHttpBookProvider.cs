using System;
using System.Collections.Concurrent;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges.Coinbase;

public class CoinbaseHttpBookProvider : BackgroundService, IBookProvider
{
    private readonly ILogger<CoinbaseHttpBookProvider> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly CoinbaseClient _coinbaseClient;
    private readonly ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)> _orderBooks = new();
    
    private readonly Dictionary<string, string> _symbolMapping = new();

    private string _status = "Disconnected";
    private string? _lastError;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public string ExchangeName => "Coinbase";

    public CoinbaseHttpBookProvider(
        ILogger<CoinbaseHttpBookProvider> logger,
        ChannelProvider channelProvider,
        CoinbaseClient coinbaseClient)
    {
        _logger = logger;
        _channelProvider = channelProvider;
        _coinbaseClient = coinbaseClient;

        // Initialize mapping from Centralized Source
        foreach (var pair in TradingPair.CommonPairs)
        {
            _symbolMapping[pair.Symbol] = pair.GetCoinbaseSymbol();
        }
    }

    public (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)? GetOrderBook(string symbol)
    {
        return _orderBooks.TryGetValue(symbol, out var book) ? book : null;
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync() => _coinbaseClient.GetSpotFeesAsync();
    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync() => _coinbaseClient.GetCachedFeesAsync();

    public ConnectionStatus GetConnectionStatus()
    {
        return new ConnectionStatus
        {
            ExchangeName = "Coinbase",
            Status = _status,
            LastUpdate = _lastUpdate,
            ErrorMessage = _lastError
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Coinbase HTTP Book Provider started polling...");
        _status = "Connected";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var symbol in _symbolMapping.Keys)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var coinbaseSymbol = _symbolMapping[symbol];
                    var book = await _coinbaseClient.GetOrderBookAsync(coinbaseSymbol, 20);

                    if (book != null)
                    {
                        _orderBooks[symbol] = (book.Value.Bids, book.Value.Asks, DateTime.UtcNow);
                        _lastUpdate = DateTime.UtcNow;
                        _lastError = null;
                        _status = "Connected";
                        
                        // Notify detection service
                        _channelProvider.MarketUpdateChannel.Writer.TryWrite(symbol);
                    }

                    // Throttle between individual product requests to avoid rate limits
                    await Task.Delay(200, stoppingToken);
                }

                // Wait before next full cycle
                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _status = "Error";
                _lastError = ex.Message;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogError(ex, "Error in Coinbase HTTP Book Provider polling loop");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
