using System;
using System.Net.WebSockets;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services.Exchanges.Coinbase;

public class CoinbaseWsClient : BackgroundService, IBookProvider
{
    private readonly ILogger<CoinbaseWsClient> _logger;
    private readonly ChannelProvider _channelProvider;
    private readonly IExchangeClient _coinbaseClient; // Changed from CoinbaseClient to IExchangeClient
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly ConcurrentDictionary<string, (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)> _orderBooks = new(); // Changed to ConcurrentDictionary
    private readonly string _wsUrl = "wss://advanced-trade-ws.coinbase.com";
    
    private readonly Dictionary<string, string> _symbolMapping = new();

    private readonly Dictionary<string, string> _reverseMapping;

    private string _status = "Disconnected";
    private string? _lastError;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public string ExchangeName => "Coinbase";

    public CoinbaseWsClient(
        ILogger<CoinbaseWsClient> logger, 
        ChannelProvider channelProvider, 
        CoinbaseClient coinbaseClient,
        IConfiguration configuration)
    {
        Console.WriteLine("DEBUG: CoinbaseWsClient Constructor START");
        _logger = logger;
        _channelProvider = channelProvider;
        _coinbaseClient = coinbaseClient;
        _apiKey = configuration["Coinbase:ApiKey"] ?? string.Empty;
        _apiSecret = configuration["Coinbase:ApiSecret"] ?? string.Empty;

        // Initialize mapping from Centralized Source
        foreach (var pair in TradingPair.CommonPairs)
        {
            var coinbaseSymbol = pair.GetCoinbaseSymbol();
            _symbolMapping[pair.Symbol] = coinbaseSymbol;
        }

        _reverseMapping = _symbolMapping.ToDictionary(kv => kv.Value, kv => kv.Key);
        Console.WriteLine("DEBUG: CoinbaseWsClient Constructor END");
    }

    public (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks, DateTime LastUpdate)? GetOrderBook(string symbol)
    {
        return _orderBooks.TryGetValue(symbol, out var book) ? book : null; // Removed .ToUpper()
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
        Console.WriteLine("DEBUG: CoinbaseWsClient ExecuteAsync START");
        _logger.LogInformation("Coinbase: ExecuteAsync background worker started");
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
                _logger.LogInformation("Coinbase: Connected successfully");

                // Subscribe to level2 updates for order book data
                var productIds = _symbolMapping.Values.ToArray();
                var channel = "level2";
                
                object subscribeMsg;
                
                if (_apiSecret.Contains("BEGIN")) // Likely a CDP key (PEM format)
                {
                    var jwt = GenerateJwt("GET", "coinbase-cloud", "level2"); // Channel level auth
                    subscribeMsg = new
                    {
                        type = "subscribe",
                        product_ids = productIds,
                        channel = channel,
                        jwt = jwt
                    };
                }
                else // Legacy HMAC auth
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                    var signature = Sign(timestamp, channel, productIds);
                    subscribeMsg = new
                    {
                        type = "subscribe",
                        product_ids = productIds,
                        channel = channel,
                        api_key = _apiKey,
                        timestamp = timestamp,
                        signature = signature
                    };
                }

                var json = JsonSerializer.Serialize(subscribeMsg);
                _logger.LogInformation("Coinbase: Sending subscription message: {Json}", json);
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, stoppingToken);

                var buffer = new byte[1024 * 256]; // 256KB buffer
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    int chunks = 0;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                        chunks++;
                        
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    if (chunks > 1) 
                        _logger.LogInformation("Coinbase: Aggregated {Chunks} chunks ({Size} bytes)", chunks, ms.Length);

                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    
                    if (!string.IsNullOrEmpty(message))
                    {
                        _logger.LogInformation("Coinbase: Full message received ({Size} bytes)", message.Length);
                        ProcessMessage(message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Coinbase WebSocket client stopping...");
                break;
            }
            catch (Exception ex)
            {
                _status = "Error";
                _lastError = ex.Message;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogError(ex, "Coinbase WebSocket error: {Message}. Stack: {Stack}", ex.Message, ex.StackTrace);
                if (ex.InnerException != null)
                {
                    _logger.LogError("Coinbase Inner Exception: {Inner}", ex.InnerException.Message);
                }
                try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private string Sign(string timestamp, string channel, string[] productIds)
    {
        var preHash = timestamp + channel + string.Join(",", productIds);
        var secretBytes = Encoding.UTF8.GetBytes(_apiSecret);
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(preHash));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private string GenerateJwt(string method, string issuer, string channel)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expirationTime = now + 120;
        var uri = $"{method} api.coinbase.com/api/v3/brokerage/{channel}"; // For WS, this varies by doc version but level2 is common

        var privateKeyBytes = ExtractPrivateKeyFromPem(_apiSecret);

        using (var key = ECDsa.Create())
        {
            try { key.ImportECPrivateKey(privateKeyBytes, out _); }
            catch { key.ImportPkcs8PrivateKey(privateKeyBytes, out _); }

            var header = new Dictionary<string, object>
            {
                { "alg", "ES256" },
                { "typ", "JWT" },
                { "kid", _apiKey },
                { "nonce", Guid.NewGuid().ToString("N") }
            };

            var payload = new Dictionary<string, object>
            {
                { "sub", _apiKey },
                { "iss", "coinbase-cloud" },
                { "nbf", now },
                { "exp", expirationTime }
                // Omitting uri claim for WebSocket as per Coinbase CDP docs
            };

            return EncodeJwt(header, payload, key);
        }
    }

    private string EncodeJwt(Dictionary<string, object> header, Dictionary<string, object> payload, ECDsa key)
    {
        var headerJson = JsonSerializer.Serialize(header);
        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        var payloadJson = JsonSerializer.Serialize(payload);
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signatureInput = $"{encodedHeader}.{encodedPayload}";
        var signatureBytes = key.SignData(Encoding.UTF8.GetBytes(signatureInput), HashAlgorithmName.SHA256);
        var encodedSignature = Base64UrlEncode(signatureBytes);

        return $"{signatureInput}.{encodedSignature}";
    }

    private string Base64UrlEncode(byte[] data) => Convert.ToBase64String(data).Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private byte[] ExtractPrivateKeyFromPem(string pemKey)
    {
        var base64 = new StringBuilder();
        var lines = pemKey.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (!line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
                base64.Append(line);
        }
        return Convert.FromBase64String(base64.ToString());
    }

    private void ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("type", out var type))
            {
               var typeStr = type.GetString();
               if (typeStr == "subscriptions" || typeStr == "error")
               {
                   _logger.LogInformation("Coinbase WS Event: {Type} - {Msg}", typeStr, message.Length > 500 ? message.Substring(0, 500) : message);
               }
            }

            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "error")
            {
                _logger.LogWarning("Coinbase WS Error Message: {Message}", message.Substring(0, 100));
                return;
            }

            if (root.TryGetProperty("channel", out var channel))
            {
                var channelStr = channel.GetString();
                _logger.LogInformation("Coinbase: Processing channel={Channel}, hasEvents={HasEvents}", 
                    channelStr, root.TryGetProperty("events", out _));
                    
                if (channelStr == "level2")
                {
                    if (!root.TryGetProperty("events", out var events))
                    {
                        _logger.LogWarning("Coinbase: level2 message has no 'events' property");
                        return;
                    }

                foreach (var ev in events.EnumerateArray())
                {
                    if (!ev.TryGetProperty("product_id", out var productIdProp)) 
                    {
                        _logger.LogWarning("Coinbase: event has no 'product_id'");
                        continue;
                    }
                    var coinbaseSymbol = productIdProp.GetString();
                    if (string.IsNullOrEmpty(coinbaseSymbol) || !_reverseMapping.TryGetValue(coinbaseSymbol, out var symbol)) 
                    {
                        _logger.LogWarning("Coinbase: Unmapped or null product_id: {CoinbaseSymbol}", coinbaseSymbol);
                        continue;
                    }

                    if (!ev.TryGetProperty("updates", out var updates)) 
                    {
                        _logger.LogWarning("Coinbase: event for {Symbol} has no 'updates'", symbol);
                        continue;
                    }
                    var eventType = ev.TryGetProperty("type", out var eventTypeProp) ? eventTypeProp.GetString() : "update";
                    _logger.LogInformation("Coinbase: Processing {EventType} for {Symbol} ({Count} updates)", eventType, symbol, updates.GetArrayLength());
                    Dictionary<decimal, decimal> bids;
                    Dictionary<decimal, decimal> asks;

                    if (eventType == "snapshot")
                    {
                        bids = new Dictionary<decimal, decimal>();
                        asks = new Dictionary<decimal, decimal>();
                    }
                    else
                    {
                        var currentBook = _orderBooks.TryGetValue(symbol, out var b) ? b : (Bids: new List<(decimal, decimal)>(), Asks: new List<(decimal, decimal)>(), LastUpdate: DateTime.MinValue);
                        bids = currentBook.Bids.ToDictionary(x => x.Item1, x => x.Item2);
                        asks = currentBook.Asks.ToDictionary(x => x.Item1, x => x.Item2);
                    }

                    foreach (var update in updates.EnumerateArray())
                    {
                        var side = update.GetProperty("side").GetString();
                        var price = decimal.Parse(update.GetProperty("price_level").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        var quantity = decimal.Parse(update.GetProperty("new_quantity").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

                        if (side == "bid")
                        {
                            if (quantity == 0) bids.Remove(price);
                            else bids[price] = quantity;
                        }
                        else
                        {
                            if (quantity == 0) asks.Remove(price);
                            else asks[price] = quantity;
                        }
                    }

                    // Sort and limit
                    var sortedBids = bids.OrderByDescending(x => x.Key).Take(20).Select(x => (x.Key, x.Value)).ToList();
                    var sortedAsks = asks.OrderBy(x => x.Key).Take(20).Select(x => (x.Key, x.Value)).ToList();

                    _orderBooks[symbol] = (sortedBids, sortedAsks, DateTime.UtcNow);
                    _lastUpdate = DateTime.UtcNow;
                    _logger.LogInformation("Coinbase: Updated order book for {Symbol}. Bids: {BidsCount}, Asks: {AsksCount}", 
                        symbol, sortedBids.Count, sortedAsks.Count);
                    
                    // Notify ArbitrageDetectionService
                    _channelProvider.MarketUpdateChannel.Writer.TryWrite(symbol);
                }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error processing Coinbase WS message: {Error}", ex.Message);
        }
    }
}
