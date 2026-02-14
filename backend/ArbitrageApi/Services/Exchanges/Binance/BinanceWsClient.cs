using System;
using System.Net.WebSockets;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges.Binance;

public class BinanceWsClient : BackgroundService, IBookProvider
{
    private readonly ILogger<BinanceWsClient> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly IExchangeClient _binanceClient; // Use existing client for fees and potentially initial book
    private readonly ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)> _orderBooks = new();
    private readonly string _wsUrl = "wss://stream.binance.com:9443/stream";
    private readonly List<string> _symbols;

    private string _status = "Disconnected";
    private string? _lastError;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public string ExchangeName => "Binance";

    public BinanceWsClient(
        ILogger<BinanceWsClient> logger, 
        ChannelProvider channelProvider, 
        IEnumerable<IExchangeClient> clients)
    {
        _logger = logger;
        _channelProvider = channelProvider;
        _binanceClient = clients.First(c => c.ExchangeName == "Binance");

        // Initialize symbols from Centralized Source
        _symbols = TradingPair.CommonPairs.Select(p => p.Symbol).ToList();
    }

    public (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)? GetOrderBook(string symbol)
    {
        return _orderBooks.TryGetValue(symbol.ToUpper(), out var book) ? book : null;
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync() => _binanceClient.GetSpotFeesAsync();
    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync() => _binanceClient.GetCachedFeesAsync();

    public ConnectionStatus GetConnectionStatus()
    {
        return new ConnectionStatus
        {
            ExchangeName = "Binance",
            Status = _status,
            LastUpdate = _lastUpdate,
            ErrorMessage = _lastError
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _status = "Connecting";
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(_wsUrl), stoppingToken);
                _status = "Connected";
                _lastUpdate = DateTime.UtcNow;
                _lastError = null;
                _logger.LogInformation("Connected to Binance WebSocket");

                // Subscribe to depth streams
                var subscribeMsg = new
                {
                    method = "SUBSCRIBE",
                    @params = _symbols.Select(s => $"{s.ToLower()}@depth20@100ms").ToArray(),
                    id = 1
                };

                var json = JsonSerializer.Serialize(subscribeMsg);
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, stoppingToken);

                var buffer = new byte[1024 * 16];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    ms.Seek(0, SeekOrigin.Begin);
                    using var reader = new StreamReader(ms, Encoding.UTF8);
                    var message = await reader.ReadToEndAsync();
                    
                    if (!string.IsNullOrEmpty(message))
                    {
                        ProcessMessage(message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Binance WebSocket client stopping...");
                break;
            }
            catch (Exception ex)
            {
                _status = "Error";
                _lastError = ex.Message;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogError(ex, "Binance WebSocket error. Reconnecting...");
                try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            _logger.LogTrace("Binance WS Message: {Message}", message);
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.TryGetProperty("stream", out var stream) && stream.GetString()?.Contains("@depth") == true)
            {
                var data = root.GetProperty("data");
                var symbol = stream.GetString()?.Split('@')[0].ToUpper();
                if (string.IsNullOrEmpty(symbol)) return;

                var bids = data.GetProperty("bids").EnumerateArray()
                    .Select(b => (decimal.Parse(b[0].GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(b[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();

                var asks = data.GetProperty("asks").EnumerateArray()
                    .Select(a => (decimal.Parse(a[0].GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(a[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();

                _orderBooks[symbol] = (bids, asks, DateTime.UtcNow);
                _lastUpdate = DateTime.UtcNow;
                _logger.LogDebug("Binance: Updated order book for {Symbol}", symbol);
                
                // Notify ArbitrageDetectionService
                _channelProvider.MarketUpdateChannel.Writer.TryWrite(symbol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error processing Binance WS message: {Error}", ex.Message);
        }
    }
}
