using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class BinanceWebSocketStream : WebSocketPriceStreamBase
{
    private readonly List<string> _symbols;
    private readonly string _wsUrl = "wss://stream.binance.com:9443/stream";

    public BinanceWebSocketStream(ILogger<BinanceWebSocketStream> logger, List<string> symbols) : base(logger)
    {
        _symbols = symbols;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var streams = string.Join("/", _symbols.Select(s => $"{s.ToLower()}@depth@100ms"));
            var url = $"{_wsUrl}?streams={streams}";

            Logger.LogInformation("Connecting to Binance WebSocket: {Url}", _wsUrl);
            await _webSocket.ConnectAsync(new Uri(url), _cancellationTokenSource.Token);

            Logger.LogInformation("Connected to Binance WebSocket");
            await ReadWebSocketMessages(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting Binance WebSocket stream");
            throw;
        }
    }

    protected override async Task ProcessWebSocketMessage(string json, CancellationToken cancellationToken)
    {
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var data))
                {
                    var symbol = data.GetProperty("s").GetString();
                    var bids = data.GetProperty("b").EnumerateArray()
                        .Take(20)
                        .Select(b => (
                            Price: decimal.Parse(b[0].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                            Quantity: decimal.Parse(b[1].GetString(), System.Globalization.CultureInfo.InvariantCulture)
                        ))
                        .ToList();

                    var asks = data.GetProperty("a").EnumerateArray()
                        .Take(20)
                        .Select(a => (
                            Price: decimal.Parse(a[0].GetString(), System.Globalization.CultureInfo.InvariantCulture),
                            Quantity: decimal.Parse(a[1].GetString(), System.Globalization.CultureInfo.InvariantCulture)
                        ))
                        .ToList();

                    lock (_orderBookLock)
                    {
                        _orderBooks[symbol] = (bids, asks);
                    }

                    if (bids.Count > 0 && asks.Count > 0)
                    {
                        var midPrice = (bids[0].Price + asks[0].Price) / 2;
                        OnPriceUpdated(new WebSocketPriceUpdate
                        {
                            Symbol = symbol,
                            Price = midPrice,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Binance WebSocket message");
        }
    }
}
