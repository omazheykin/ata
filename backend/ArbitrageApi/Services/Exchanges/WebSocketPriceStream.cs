using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges;

public class WebSocketPriceUpdate
{
    public string Symbol { get; set; }
    public decimal Price { get; set; }
    public DateTime Timestamp { get; set; }
}

public interface IWebSocketPriceStream
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    bool IsConnected { get; }
    (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)? GetLatestOrderBook(string symbol);
    event EventHandler<WebSocketPriceUpdate>? PriceUpdated;
}

public abstract class WebSocketPriceStreamBase : IWebSocketPriceStream
{
    protected readonly ILogger Logger;
    protected ClientWebSocket _webSocket;
    protected CancellationTokenSource _cancellationTokenSource;
    protected readonly ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)> _orderBooks;
    protected readonly object _orderBookLock = new();

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public event EventHandler<WebSocketPriceUpdate>? PriceUpdated;

    protected WebSocketPriceStreamBase(ILogger logger)
    {
        Logger = logger;
        _webSocket = new ClientWebSocket();
        _orderBooks = new ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)>();
    }

    public abstract Task StartAsync(CancellationToken cancellationToken);

    public async Task StopAsync()
    {
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            _webSocket?.Dispose();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping WebSocket stream");
        }
    }

    public (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)? GetLatestOrderBook(string symbol)
    {
        lock (_orderBookLock)
        {
            _orderBooks.TryGetValue(symbol, out var book);
            return book.Bids?.Count > 0 ? book : null;
        }
    }

    protected void OnPriceUpdated(WebSocketPriceUpdate update)
    {
        PriceUpdated?.Invoke(this, update);
    }

    protected async Task ReadWebSocketMessages(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 4];
        try
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessWebSocketMessage(json, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("WebSocket read operation cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reading WebSocket messages");
        }
    }

    protected abstract Task ProcessWebSocketMessage(string json, CancellationToken cancellationToken);
}
