using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArbitrageApi.Services.Exchanges;

public class CoinbaseWebSocketStream : WebSocketPriceStreamBase
{
    private readonly List<string> _symbols;
    private readonly string _wsUrl = "wss://ws-feed.exchange.coinbase.com";
    private readonly Dictionary<string, string> _symbolMapping;

    public CoinbaseWebSocketStream(ILogger<CoinbaseWebSocketStream> logger, List<string> symbols, Dictionary<string, string> symbolMapping) : base(logger)
    {
        _symbols = symbols;
        _symbolMapping = symbolMapping;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var productIds = _symbols
                .Select(s => _symbolMapping.TryGetValue(s, out var mapped) ? mapped : s)
                .Distinct()
                .ToList();

            Logger.LogInformation("Connecting to Coinbase WebSocket");
            await _webSocket.ConnectAsync(new Uri(_wsUrl), _cancellationTokenSource.Token);

            var subscribeMessage = new
            {
                type = "subscribe",
                product_ids = productIds,
                channels = new[] { "ticker", "level2" }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(subscribeMessage);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

            Logger.LogInformation("Subscribed to Coinbase WebSocket channels");
            await ReadWebSocketMessages(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting Coinbase WebSocket stream");
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

                if (!root.TryGetProperty("type", out var typeElement))
                    return;

                var type = typeElement.GetString();

                if (type == "l2update")
                {
                    ProcessL2Update(root);
                }
                else if (type == "ticker")
                {
                    ProcessTicker(root);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Coinbase WebSocket message");
        }
    }

    private void ProcessL2Update(JsonElement root)
    {
        if (!root.TryGetProperty("product_id", out var productIdElement))
            return;

        var productId = productIdElement.GetString();
        var symbol = ReverseSymbolMapping(productId);

        if (root.TryGetProperty("changes", out var changesElement))
        {
            var bids = new List<(decimal Price, decimal Quantity)>();
            var asks = new List<(decimal Price, decimal Quantity)>();

            foreach (var change in changesElement.EnumerateArray())
            {
                var changeArray = change.EnumerateArray().ToList();
                if (changeArray.Count >= 3)
                {
                    var side = changeArray[0].GetString();
                    var price = decimal.Parse(changeArray[1].GetString(), System.Globalization.CultureInfo.InvariantCulture);
                    var quantity = decimal.Parse(changeArray[2].GetString(), System.Globalization.CultureInfo.InvariantCulture);

                    if (side == "buy")
                        bids.Add((price, quantity));
                    else if (side == "sell")
                        asks.Add((price, quantity));
                }
            }

            if (bids.Count > 0 || asks.Count > 0)
            {
                if (_orderBooks.TryGetValue(productId, out var existing))
                {
                    var updatedBids = MergeOrderBookSide(existing.Bids, bids, true);
                    var updatedAsks = MergeOrderBookSide(existing.Asks, asks, false);
                    _orderBooks[productId] = (updatedBids.Take(20).ToList(), updatedAsks.Take(20).ToList());
                }
                else
                {
                    _orderBooks[productId] = (bids.Take(20).ToList(), asks.Take(20).ToList());
                }
            }
        }
    }

    private void ProcessTicker(JsonElement root)
    {
        if (!root.TryGetProperty("product_id", out var productIdElement) ||
            !root.TryGetProperty("price", out var priceElement))
            return;

        var productId = productIdElement.GetString();
        var symbol = ReverseSymbolMapping(productId);
        var price = decimal.Parse(priceElement.GetString(), System.Globalization.CultureInfo.InvariantCulture);

        OnPriceUpdated(new WebSocketPriceUpdate
        {
            Symbol = symbol,
            Price = price,
            Timestamp = DateTime.UtcNow
        });
    }

    private List<(decimal Price, decimal Quantity)> MergeOrderBookSide(
        List<(decimal Price, decimal Quantity)> existing,
        List<(decimal Price, decimal Quantity)> updates,
        bool isBid)
    {
        var merged = new Dictionary<decimal, decimal>(existing.ToDictionary(x => x.Price, x => x.Quantity));

        foreach (var (price, quantity) in updates)
        {
            if (quantity == 0)
                merged.Remove(price);
            else
                merged[price] = quantity;
        }

        var sorted = isBid
            ? merged.OrderByDescending(x => x.Key).ToList()
            : merged.OrderBy(x => x.Key).ToList();

        return sorted.Select(x => (x.Key, x.Value)).ToList();
    }

    private string ReverseSymbolMapping(string productId)
    {
        return _symbolMapping.FirstOrDefault(x => x.Value == productId).Key ?? productId;
    }
}
