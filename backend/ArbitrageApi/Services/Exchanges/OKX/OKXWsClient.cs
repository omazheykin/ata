using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;

namespace ArbitrageApi.Services.Exchanges.OKX;

public class OKXWsClient : BackgroundService, IBookProvider
{
    private readonly ILogger<OKXWsClient> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly IExchangeClient _okxClient;
    private readonly ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)> _orderBooks = new();
    private readonly string _wsUrl = "wss://ws.okx.com:8443/ws/v5/public";
    private readonly List<TradingPair> _pairs;

    private string _status = "Disconnected";
    private string? _lastError;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public string ExchangeName => "OKX";

    public OKXWsClient(
        ILogger<OKXWsClient> logger,
        ChannelProvider channelProvider,
        IEnumerable<IExchangeClient> clients)
    {
        _logger = logger;
        _channelProvider = channelProvider;
        _okxClient = clients.First(c => c.ExchangeName == "OKX");
        _pairs = TradingPair.CommonPairs;
    }

    public (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)? GetOrderBook(string symbol)
    {
        // Internal dictionary uses standard symbol (e.g., BTCUSDT)
        return _orderBooks.TryGetValue(symbol.ToUpper(), out var book) ? book : null;
    }

    public Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync() => _okxClient.GetSpotFeesAsync();
    public Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync() => _okxClient.GetCachedFeesAsync();

    public ConnectionStatus GetConnectionStatus()
    {
        return new ConnectionStatus
        {
            ExchangeName = "OKX",
            Status = _status,
            LastUpdate = _lastUpdate,
            ErrorMessage = _lastError
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OKX WebSocket Client starting...");

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
                _logger.LogInformation("Connected to OKX WebSocket");

                // Subscribe to order book channels
                var subscribeMsg = new
                {
                    op = "subscribe",
                    args = _pairs.Select(p => new { channel = "books5", instId = p.GetOKXSymbol() }).ToArray()
                };

                var json = JsonSerializer.Serialize(subscribeMsg);
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, stoppingToken);

                var buffer = new byte[1024 * 32];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result = null!;
                    do
                    {
                        if (ws.State != WebSocketState.Open) break;
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage && ws.State == WebSocketState.Open);

                    if (result == null || result.MessageType == WebSocketMessageType.Close) break;

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
                break;
            }
            catch (Exception ex)
            {
                _status = "Error";
                _lastError = ex.Message;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogError(ex, "OKX WebSocket error. Reconnecting...");
                try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            _logger.LogTrace("OKX WS Message: {Message}", message);
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.TryGetProperty("event", out var eventProp))
            {
                var eventType = eventProp.GetString();
                if (eventType == "subscribe")
                {
                    _logger.LogInformation("OKX: Successfully subscribed to channel");
                }
                else if (eventType == "error")
                {
                    _logger.LogError("OKX: WebSocket Error: {Message}", message);
                }
                return;
            }

            // OKX events: { "arg": { "channel": "books", "instId": "BTC-USDT" }, "action": "snapshot", "data": [...] }
            if (root.TryGetProperty("arg", out var arg) && root.TryGetProperty("data", out var dataArray))
            {
                var channel = arg.GetProperty("channel").GetString();
                if (channel != "books5") return;

                var instId = arg.GetProperty("instId").GetString();
                var standardSymbol = _pairs.FirstOrDefault(p => p.GetOKXSymbol() == instId)?.Symbol;
                
                if (string.IsNullOrEmpty(standardSymbol)) return;

                var data = dataArray.EnumerateArray().FirstOrDefault();
                if (data.ValueKind == JsonValueKind.Undefined) return;

                var bidsElement = data.GetProperty("bids");
                var asksElement = data.GetProperty("asks");

                var bids = bidsElement.EnumerateArray()
                    .Select(b => (decimal.Parse(b[0].GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(b[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .Take(20)
                    .ToList();

                var asks = asksElement.EnumerateArray()
                    .Select(a => (decimal.Parse(a[0].GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(a[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                    .Take(20)
                    .ToList();

                _orderBooks[standardSymbol] = (bids, asks);
                _lastUpdate = DateTime.UtcNow;
                _logger.LogDebug("OKX: Updated order book for {Symbol} (Bids: {BidCount}, Asks: {AskCount})", standardSymbol, bids.Count, asks.Count);
                
                // Notify ArbitrageDetectionService
                _channelProvider.MarketUpdateChannel.Writer.TryWrite(standardSymbol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OKX WS message");
        }
    }
}
