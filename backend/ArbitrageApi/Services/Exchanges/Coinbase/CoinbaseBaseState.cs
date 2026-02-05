using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges.Coinbase;

namespace ArbitrageApi.Services.Exchanges;

public abstract class CoinbaseBaseState : IExchangeState
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;
    protected readonly string ApiKey;
    protected readonly string ApiSecret;
    protected readonly string BaseUrl;
    protected readonly string ExchangeName = "Coinbase";

    protected Dictionary<string, string> SymbolMapping = new();

    private (decimal Maker, decimal Taker)? _cachedFees;
    private DateTime _lastFeeUpdate = DateTime.MinValue;
    private readonly TimeSpan _feeTtl = TimeSpan.FromMinutes(5);
    protected List<Balance> CachedBalances = new();

    protected CoinbaseBaseState(HttpClient httpClient, ILogger logger, string apiKey, string apiSecret, string baseUrl)
    {
        HttpClient = httpClient;
        Logger = logger;
        ApiKey = apiKey;
        ApiSecret = apiSecret;
        BaseUrl = baseUrl;

        // Initialize mapping from Centralized Source
        foreach (var pair in TradingPair.CommonPairs)
        {
            SymbolMapping[pair.Symbol] = pair.GetCoinbaseSymbol();
        }
    }

    public abstract Task<ExchangePrice?> GetPriceAsync(string symbol);

    public virtual async Task<Dictionary<string, ExchangePrice>> GetPricesAsync(List<string> symbols)
    {
        var prices = new Dictionary<string, ExchangePrice>();
        foreach (var symbol in symbols)
        {
            var price = await GetPriceAsync(symbol);
            if (price != null) prices[symbol] = price;
            await Task.Delay(100);
        }
        return prices;
    }

    public abstract Task<(decimal Maker, decimal Taker)?> GetSpotFeesAsync();

    public virtual async Task<(decimal Maker, decimal Taker)?> GetCachedFeesAsync()
    {
        if (_cachedFees != null && (DateTime.UtcNow - _lastFeeUpdate) < _feeTtl)
        {
            Logger.LogDebug("💰 [Coinbase] Using cached fees: Maker={Maker}, Taker={Taker}", _cachedFees.Value.Maker, _cachedFees.Value.Taker);
            return _cachedFees;
        }

        Logger.LogInformation("🔄 [Coinbase] Fetching fresh fees from API...");
        var fees = await GetSpotFeesAsync();
        if (fees != null)
        {
            _cachedFees = fees;
            _lastFeeUpdate = DateTime.UtcNow;
            Logger.LogInformation("✅ [Coinbase] Fees retrieved: Maker={Maker}, Taker={Taker}", fees.Value.Maker, fees.Value.Taker);
        }
        else
        {
            Logger.LogWarning("⚠️ [Coinbase] Failed to retrieve fees, using default: 0.001 (0.1%)");
        }
        return _cachedFees ?? (0.001m, 0.001m); // Lowered default for visibility
    }
    public abstract Task<List<Balance>> GetBalancesAsync();

    public virtual async Task<(List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks)?> GetOrderBookAsync(string symbol, int limit = 20)
    {
        try
        {
            var apiSymbol = SymbolMapping.TryGetValue(symbol, out var mapped) ? mapped : symbol;

            Logger.LogInformation("Fetching order book for {Symbol} from Coinbase", apiSymbol);

            var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
            var response = await cats.GetOrderBookAsync(apiSymbol, 100);

            Logger.LogDebug("Order book fetched for {Symbol} from Coinbase", apiSymbol);

            if (response?.PriceBook == null) return null;
            
            var bids = response.PriceBook.Bids.Take(limit)
                .Select(b => (decimal.Parse(b.Price ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                              decimal.Parse(b.Size ?? "0", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            var asks = response.PriceBook.Asks.Take(limit)
                .Select(a => (decimal.Parse(a.Price ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                              decimal.Parse(a.Size ?? "0", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            Logger.LogDebug("Asks processed for {Symbol} from Coinbase", apiSymbol);

            return (bids, asks);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fetching order book from Coinbase for {Symbol}", symbol);
            return null;
        }
    }

    protected (List<(decimal Price, decimal Quantity)> Bids, List<(decimal Price, decimal Quantity)> Asks) GetSimulatedOrderBook(decimal midPrice, int limit = 20)
    {
        var bids = new List<(decimal Price, decimal Quantity)>();
        var asks = new List<(decimal Price, decimal Quantity)>();

        var random = new Random();
        var spread = midPrice * 0.001m; // 0.1% spread

        for (int i = 0; i < limit; i++)
        {
            // Bids (Buy orders, below mid price)
            var bidPrice = midPrice - (spread / 2) - (i * midPrice * 0.0005m);
            var bidQty = (decimal)(random.NextDouble() * 2.0 + 0.1);
            bids.Add((Math.Round(bidPrice, 8), Math.Round(bidQty, 4)));

            // Asks (Sell orders, above mid price)
            var askPrice = midPrice + (spread / 2) + (i * midPrice * 0.0005m);
            var askQty = (decimal)(random.NextDouble() * 2.0 + 0.1);
            asks.Add((Math.Round(askPrice, 8), Math.Round(askQty, 4)));
        }

        return (bids, asks);
    }

    public async Task UpdateSymbolMappingWithSupportedProductsAsync()
    {
        try
        {
            var cats = new CoinbaseAdvancedTradeService(ApiKey, ApiSecret, Logger);
            var products = await cats.GetProductsAsync();
            Logger.LogInformation("Coinbase products fetched: {Count}", products.Count);

            var supportedSymbols = new HashSet<string>(products.Select(p => p.ProductId));

            SymbolMapping = SymbolMapping
                .Where(kv => supportedSymbols.Contains(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            Logger.LogInformation("Coinbase supported symbols updated: {Symbols}", string.Join(", ", SymbolMapping.Keys));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating Coinbase supported symbols");
        }
    }

    protected string Sign(string timestamp, string method, string requestPath, string body, bool isBase64Secret)
    {
        var message = timestamp + method + requestPath + body;
        byte[] secretBytes;
        try
        {
            secretBytes = isBase64Secret ? Convert.FromBase64String(ApiSecret) : Encoding.UTF8.GetBytes(ApiSecret);
        }
        catch
        {
            secretBytes = Encoding.UTF8.GetBytes(ApiSecret);
        }

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    // Order placement methods - to be implemented by derived classes
    public abstract Task<OrderResponse> PlaceMarketBuyOrderAsync(string symbol, decimal quantity);
    public abstract Task<OrderResponse> PlaceMarketSellOrderAsync(string symbol, decimal quantity);
    public abstract Task<OrderResponse> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price);
    public abstract Task<OrderResponse> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price);

    // Order management methods - to be implemented by derived classes
    public abstract Task<OrderInfo> GetOrderStatusAsync(string orderId);
    public abstract Task<bool> CancelOrderAsync(string orderId);

    // Sandbox management
    public abstract Task DepositSandboxFundsAsync(string asset, decimal amount);
}
