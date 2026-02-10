using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ArbitrageApi.Services.Exchanges.Coinbase
{
   /// <summary>
   /// Coinbase Advanced Trade API Service
   /// Handles JWT authentication and API requests
   /// </summary>
   public class CoinbaseAdvancedTradeService
   {
      private readonly string _keyName;
      private readonly string _keySecret;
      private readonly string _baseUrl = "https://api.coinbase.com";
      private readonly HttpClient _httpClient;
      private readonly ILogger _logger;

      public CoinbaseAdvancedTradeService(string keyName, string keySecret, ILogger logger)
      {
         _keyName = keyName;
         _keySecret = keySecret;
         _httpClient = new HttpClient();
         _logger = logger;
      }

      /// <summary>
      /// Generates a JWT token for API authentication
      /// Format: "METHOD HOST/PATH" (e.g., "GET api.coinbase.com/api/v3/brokerage/accounts")
      /// </summary>
      private string GenerateJwt(string method, string requestPath)
      {
         // 1) Validate keys
         if (string.IsNullOrWhiteSpace(_keySecret))
            throw new Exception("Coinbase API Secret (Private Key) is missing or empty in configuration.");
         if (string.IsNullOrWhiteSpace(_keyName))
            throw new Exception("Coinbase API Key Name (ID) is missing or empty in configuration.");

         var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
         var expirationTime = now + 120; // 2 minutes expiration

         // Separate path and query for URI
         var pathAndQuery = requestPath.Split('?');
         var path = pathAndQuery[0];
         
         // Create the URI in the exact format: "METHOD HOST/PATH"
         // Note: Coinbase docs say "METHOD HOST/PATH", query parameters are NOT included in the URI field of the payload
         var uri = $"{method} api.coinbase.com{path}";

         // 2) Parse the private key
         // Handle literal "\n" strings if they were pasted into JSON as escape sequences
         string normalizedKey = _keySecret.Replace("\\n", "\n");
         var privateKeyBytes = ExtractPrivateKeyFromPem(normalizedKey);

         using (var key = ECDsa.Create())
         {
            try
            {
               key.ImportECPrivateKey(privateKeyBytes, out _);
            }
            catch (Exception ex)
            {
               try
               {
                  key.ImportPkcs8PrivateKey(privateKeyBytes, out _);
               }
               catch
               {
                  throw new Exception("Failed to import Coinbase EC private key. Ensure it is in SEC1 or PKCS#8 format and the API Secret is correctly configured.", ex);
               }
            }

            // Create JWT header with alg, typ, kid, and nonce
            var header = new Dictionary<string, object>
                {
                    { "alg", "ES256" },
                    { "typ", "JWT" },
                    { "kid", _keyName },
                    { "nonce", GenerateRandomHex(16) }
                };

            // Create JWT payload - EXACT structure from Coinbase docs
            var payload = new Dictionary<string, object>
                {
                    { "sub", _keyName },
                    { "iss", "coinbase-cloud" },
                    { "nbf", now },
                    { "exp", expirationTime },
                    { "uri", uri }
                };

            // Encode and sign JWT
            return EncodeJwt(header, payload, key);
         }
      }

      /// <summary>
      /// Encodes JWT token with ES256 (ECDSA with SHA-256)
      /// </summary>
      private string EncodeJwt(Dictionary<string, object> header, Dictionary<string, object> payload, ECDsa key)
      {
         // Encode header
         var headerJson = JsonSerializer.Serialize(header);
         var headerBytes = Encoding.UTF8.GetBytes(headerJson);
         var encodedHeader = Base64UrlEncode(headerBytes);

         // Encode payload
         var payloadJson = JsonSerializer.Serialize(payload);
         var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
         var encodedPayload = Base64UrlEncode(payloadBytes);

         // Create signature input
         var signatureInput = $"{encodedHeader}.{encodedPayload}";
         var signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);

         // Sign the input with SHA256
         var signatureBytes = key.SignData(signatureInputBytes, HashAlgorithmName.SHA256);
         var encodedSignature = Base64UrlEncode(signatureBytes);

         return $"{signatureInput}.{encodedSignature}";
      }

      /// <summary>
      /// Base64 URL encoding (without padding)
      /// </summary>
      private string Base64UrlEncode(byte[] data)
      {
         return Convert.ToBase64String(data)
             .Replace("+", "-")
             .Replace("/", "_")
             .TrimEnd('=');
      }

      /// <summary>
      /// Generates random hex string for nonce
      /// </summary>
      private string GenerateRandomHex(int length)
      {
         var buffer = RandomNumberGenerator.GetBytes(length / 2);
         return BitConverter.ToString(buffer).Replace("-", "").ToLower();
      }

      /// <summary>
      /// Extracts the private key bytes from a PEM-formatted key string
      /// Handles both "-----BEGIN EC PRIVATE KEY-----" and "-----BEGIN PRIVATE KEY-----" formats
      /// </summary>
      private byte[] ExtractPrivateKeyFromPem(string pemKey)
      {
         try
         {
            // Remove PEM headers and footers
            var lines = pemKey.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var base64 = new StringBuilder();

            bool isPkcs8 = false;
            foreach (var line in lines)
            {
               if (line.Contains("BEGIN PRIVATE KEY")) isPkcs8 = true;
               if (!line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
               {
                  base64.Append(line);
               }
            }

            var keyBytes = Convert.FromBase64String(base64.ToString());

            // If it's PKCS#8, we might need to extract the inner EC private key
            // However, ECDsa.ImportECPrivateKey expects SEC1 format.
            // ECDsa.ImportPkcs8PrivateKey expects PKCS#8 format.
            // Let's try to detect and use the right one.
            
            using (var key = ECDsa.Create())
            {
               try 
               {
                  if (isPkcs8)
                  {
                     key.ImportPkcs8PrivateKey(keyBytes, out _);
                  }
                  else
                  {
                     key.ImportECPrivateKey(keyBytes, out _);
                  }
                  // If we reach here, the format was correct for the method used.
                  // But we need to return the bytes that ImportECPrivateKey can use later, 
                  // or just change how we use it.
                  // Actually, let's just return the bytes and handle the import in GenerateJwt.
                  return keyBytes;
               }
               catch
               {
                  // Try the other one if first failed
                  return keyBytes; 
               }
            }
         }
         catch (Exception ex)
         {
            throw new Exception("Failed to parse private key from PEM format. Ensure the key is a valid EC private key.", ex);
         }
      }

      public async Task<string> PingAsync()
      {
         var requestPath = "/api/v3/brokerage/ping";
         var method = "GET";

         try
         {
            var jwt = GenerateJwt(method, requestPath);

            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
            {
               request.Headers.Add("Authorization", $"Bearer {jwt}");

               using (var response = await _httpClient.SendAsync(request))
               {
                  var content = await response.Content.ReadAsStringAsync();

                  if (!response.IsSuccessStatusCode)
                  {
                     throw new Exception($"API Error ({response.StatusCode}): {content}");
                  }

                  return content;
               }
            }
         }
         catch (Exception ex)
         {
            throw new Exception($"Error pinging Coinbase Advanced Trade API: {ex.Message}", ex);
         }
      }

      /// <summary>
      /// Gets the order book for a product using Coinbase Advanced Trade API.
      /// </summary>
      public async Task<CoinbaseOrderBookResponse> GetOrderBookWithPermissionsAsync(string productId, int limit = 20)
      {
         // According to the Coinbase Advanced Trade API docs:
         // GET /api/v3/brokerage/product_book?product_id=BTC-USD&limit=20
         var requestPath = $"/api/v3/brokerage/product_book?product_id={productId}&limit={limit}";
         var method = "GET";

         var jwt = GenerateJwt(method, requestPath);

         using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
         {
            request.Headers.Add("Authorization", $"Bearer {jwt}");

            using (var response = await _httpClient.SendAsync(request))
            {
               var content = await response.Content.ReadAsStringAsync();

               if (!response.IsSuccessStatusCode)
               {
                  throw new Exception($"API Error ({response.StatusCode}): {content}");
               }

               var orderBook = JsonSerializer.Deserialize<CoinbaseOrderBookResponse>(content);
               if (orderBook == null)
                  throw new Exception("Failed to deserialize order book response.");

               return orderBook;
            }
         }
      }

      public virtual async Task<ProductBookResponse> GetOrderBookAsync(string symbol, int limit = 20)
      {
         try
         {
            var requestPath = $"/api/v3/brokerage/market/product_book?product_id={symbol}&limit={limit}";
            var url = $"{_baseUrl}{requestPath}";

            // Generate JWT for authentication (implement GenerateJwt as in your AdvancedTradeService)
            //var jwt = GenerateJwt("GET", requestPath);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            //request.Headers.Add("Authorization", $"Bearer {jwt}");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var orderBook = System.Text.Json.JsonSerializer.Deserialize<ProductBookResponse>(content);

            return orderBook ?? new ProductBookResponse();
         }
         catch (Exception ex)
         {
            _logger.LogError($"Error fetching order book for {symbol}: {ex.Message}");
            ProductBookResponse orderBook = new ProductBookResponse
            {
               PriceBook = new PriceBook
               {
                  ProductId = symbol,
                  Bids = new List<OrderBookLevel>(),
                  Asks = new List<OrderBookLevel>(),
                  Time = DateTime.UtcNow.ToString("o"),
                  Sequence = "0"
               }
            };
            return orderBook;
         }
      }
      public async Task<List<CoinbaseProduct>> GetProductsAsync()
      {
         var requestPath = "/api/v3/brokerage/products";
         var method = "GET";

         try
         {
            var jwt = GenerateJwt(method, requestPath);

            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
            {
               request.Headers.Add("Authorization", $"Bearer {jwt}");

               using (var response = await _httpClient.SendAsync(request))
               {
                  var content = await response.Content.ReadAsStringAsync();

                  if (!response.IsSuccessStatusCode)
                  {
                     throw new Exception($"API Error ({response.StatusCode}): {content}");
                  }

                  var productsResponse = JsonSerializer.Deserialize<ProductsResponse>(content);
                  return productsResponse?.Products ?? new List<CoinbaseProduct>();
               }
            }
         }
         catch (Exception ex)
         {
            throw new Exception($"Error getting products: {ex.Message}", ex);
         }
      }
      /// <summary>
      /// Gets all accounts (user funds/balances)
      /// </summary>
      public async Task<AccountsResponse> GetAccountsAsync()
      {
         var requestPath = "/api/v3/brokerage/accounts";
         var method = "GET";

         try
         {
            var jwt = GenerateJwt(method, requestPath);

            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
            {
               request.Headers.Add("Authorization", $"Bearer {jwt}");

               using (var response = await _httpClient.SendAsync(request))
               {
                  var content = await response.Content.ReadAsStringAsync();

                  if (!response.IsSuccessStatusCode)
                  {
                     throw new Exception($"API Error ({response.StatusCode}): {content}");
                  }

                  return JsonSerializer.Deserialize<AccountsResponse>(content) ?? new AccountsResponse();
               }
            }
         }
         catch (Exception ex)
         {
            throw new Exception($"Error getting accounts: {ex.Message}", ex);
         }
      }

      /// <summary>
      /// Gets a specific account by UUID
      /// </summary>
      public async Task<Account> GetAccountAsync(string accountUuid)
      {
         var requestPath = $"/api/v3/brokerage/accounts/{accountUuid}";
         var method = "GET";

         try
         {
            var jwt = GenerateJwt(method, requestPath);

            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
            {
               request.Headers.Add("Authorization", $"Bearer {jwt}");

               using (var response = await _httpClient.SendAsync(request))
               {
                  var content = await response.Content.ReadAsStringAsync();

                  if (!response.IsSuccessStatusCode)
                  {
                     throw new Exception($"API Error ({response.StatusCode}): {content}");
                  }

                  using var doc = JsonDocument.Parse(content);
                  var accountJson = doc.RootElement.GetProperty("account").GetRawText();
                  var account = JsonSerializer.Deserialize<Account>(accountJson);
                  return account ?? throw new Exception("Failed to deserialize account.");
               }
            }
         }
         catch (Exception ex)
         {
            throw new Exception($"Error getting account {accountUuid}: {ex.Message}", ex);
         }
      }

      /// <summary>
      /// Gets a summary of user's total balances
      /// </summary>
      public async Task<Dictionary<string, decimal>> GetBalanceSummaryAsync()
      {
         _logger.LogInformation("Fetching balance summary from Coinbase");

         var accounts = await GetAccountsAsync();
         _logger.LogInformation("Fetched {Count} accounts from Coinbase", accounts?.Accounts?.Count);

         var balances = new Dictionary<string, decimal>();
         _logger.LogInformation("Processing accounts for balance summary");
         if (accounts?.Accounts == null) {
            _logger.LogInformation("Accounts list is null");
            return balances;
         }

         foreach (var account in accounts.Accounts)
         {
            var currency = account.Currency;
            if (string.IsNullOrEmpty(currency)) continue;

            var amountStr = account.AvailableBalance?.Value ?? "0";
            if (decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
               if (balances.ContainsKey(currency))
               {
                  balances[currency] += amount;
               }
               else
               {
                  balances[currency] = amount;
               }
            }
         }

         return balances;
      }

      public async Task<CoinbaseCreateOrderResponse?> CreateOrderAsync(CoinbaseOrderRequest request)
      {
         try
         {
            var json = JsonSerializer.Serialize(request);
            var jwt = GenerateJwt("POST", "api.coinbase.com/api/v3/brokerage/orders");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.coinbase.com/api/v3/brokerage/orders");
            req.Headers.Add("Authorization", $"Bearer {jwt}");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
               _logger.LogError("Failed to create Coinbase order: {StatusCode} - {Content}", response.StatusCode, content);
               return new CoinbaseCreateOrderResponse { Success = false, ErrorMessage = content };
            }

            return JsonSerializer.Deserialize<CoinbaseCreateOrderResponse>(content);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Error calling Coinbase CreateOrder");
            return new CoinbaseCreateOrderResponse { Success = false, ErrorMessage = ex.Message };
         }
      }

      public async Task<CoinbaseOrderDetailsResponse?> GetOrderAsync(string orderId)
      {
         try
         {
            var path = $"/api/v3/brokerage/orders/historical/{orderId}";
            var jwt = GenerateJwt("GET", $"api.coinbase.com{path}");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.coinbase.com{path}");
            req.Headers.Add("Authorization", $"Bearer {jwt}");

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CoinbaseOrderDetailsResponse>(content);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Error fetching Coinbase order {OrderId}", orderId);
            return null;
         }
      }

      public async Task<bool> CancelOrdersAsync(List<string> orderIds)
      {
         try
         {
            var request = new { order_ids = orderIds };
            var json = JsonSerializer.Serialize(request);
            var jwt = GenerateJwt("POST", "api.coinbase.com/api/v3/brokerage/orders/batch_cancel");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.coinbase.com/api/v3/brokerage/orders/batch_cancel");
            req.Headers.Add("Authorization", $"Bearer {jwt}");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(req);
            return response.IsSuccessStatusCode;
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Error cancelling Coinbase orders");
            return false;
         }
      }

      /// <summary>
      /// Gets a deposit address for a specific account UUID
      /// Uses Coinbase V2 API (authenticated with same credentials)
      /// </summary>
      public async Task<string?> GetDepositAddressAsync(string accountUuid)
      {
         var requestPath = $"/v2/accounts/{accountUuid}/addresses";
         var method = "POST"; // POST creates one if it doesn't exist, which is safer than GET

         try
         {
            var jwt = GenerateJwt(method, requestPath);

            using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{requestPath}"))
            {
               request.Headers.Add("Authorization", $"Bearer {jwt}");

               using (var response = await _httpClient.SendAsync(request))
               {
                  var content = await response.Content.ReadAsStringAsync();

                  if (!response.IsSuccessStatusCode)
                  {
                     // Fallback to GET if POST is restricted but GET isn't
                     _logger.LogWarning("POST to /addresses failed ({StatusCode}), trying GET...", response.StatusCode);
                     return await GetLastDepositAddressAsync(accountUuid);
                  }

                  using var doc = JsonDocument.Parse(content);
                  if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("address", out var addr))
                  {
                     return addr.GetString();
                  }
                  
                  return null;
               }
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Error getting deposit address for account {Uuid}", accountUuid);
            return null;
         }
      }

      private async Task<string?> GetLastDepositAddressAsync(string accountUuid)
      {
         var requestPath = $"/v2/accounts/{accountUuid}/addresses";
         var method = "GET";

         var jwt = GenerateJwt(method, requestPath);

         using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{requestPath}"))
         {
            request.Headers.Add("Authorization", $"Bearer {jwt}");

            using (var response = await _httpClient.SendAsync(request))
            {
               var content = await response.Content.ReadAsStringAsync();
               if (!response.IsSuccessStatusCode) return null;

               using var doc = JsonDocument.Parse(content);
               if (doc.RootElement.TryGetProperty("data", out var dataList) && dataList.GetArrayLength() > 0)
               {
                  return dataList[0].GetProperty("address").GetString();
               }
               return null;
            }
         }
      }
   }

}



public class OrderBookLevel
{
   [JsonPropertyName("price")]
   public string? Price { get; set; }

   [JsonPropertyName("size")]
   public string? Size { get; set; }
}

public class PriceBook
{
   [JsonPropertyName("product_id")]
   public string? ProductId { get; set; }

   [JsonPropertyName("bids")]
   public List<OrderBookLevel> Bids { get; set; } = new();

   [JsonPropertyName("asks")]
   public List<OrderBookLevel> Asks { get; set; } = new();

   [JsonPropertyName("time")]
   public string? Time { get; set; }

   [JsonPropertyName("sequence")]
   public string? Sequence { get; set; }
}

public class ProductBookResponse
{
   [JsonPropertyName("pricebook")]
   public PriceBook? PriceBook { get; set; }
}

public class CoinbaseOrderBookResponse
{
   [JsonPropertyName("product_id")]
   public string? ProductId { get; set; }

   [JsonPropertyName("bids")]
   public List<List<string>> Bids { get; set; } = new();

   [JsonPropertyName("asks")]
   public List<List<string>> Asks { get; set; } = new();

   [JsonPropertyName("time")]
   public string? Time { get; set; }

   [JsonPropertyName("sequence")]
   public string? Sequence { get; set; }
}

public class CoinbaseAmount
{
   [JsonPropertyName("value")]
   public string Value { get; set; } = "0";
}

public class CoinbasePriceResponse
{
   [JsonPropertyName("data")]
   public CoinbasePriceData? Data { get; set; }
}

public class CoinbasePriceData
{
   [JsonPropertyName("amount")]
   public string Amount { get; set; } = string.Empty;
}

public class CoinbaseTickerResponse
{
   [JsonPropertyName("price")]
   public string Price { get; set; } = string.Empty;
}

public class CoinbaseFeeResponse
{
   [JsonPropertyName("fee_tier")]
   public CoinbaseFeeTier? FeeTier { get; set; }
}

public class CoinbaseFeeTier
{
   [JsonPropertyName("maker_fee_rate")]
   public decimal MakerFeeRate { get; set; }
   [JsonPropertyName("taker_fee_rate")]
   public decimal TakerFeeRate { get; set; }
}

public class CoinbaseExchangeFeeResponse
{
   [JsonPropertyName("maker_fee_rate")]
   public decimal MakerFeeRate { get; set; }
   [JsonPropertyName("taker_fee_rate")]
   public decimal TakerFeeRate { get; set; }
}


public class CoinbaseProduct
{
   [JsonPropertyName("product_id")]
   public string? ProductId { get; set; }

   [JsonPropertyName("base_currency")]
   public string? BaseCurrency { get; set; }

   [JsonPropertyName("quote_currency")]
   public string? QuoteCurrency { get; set; }

   [JsonPropertyName("display_name")]
   public string? DisplayName { get; set; }

   [JsonPropertyName("status")]
   public string? Status { get; set; }

}

public class ProductsResponse
{
   [JsonPropertyName("products")]
   public List<CoinbaseProduct> Products { get; set; } = new();
}

/// <summary>
/// Account information models
/// </summary>
public class Account
{
   [JsonPropertyName("uuid")]
   public string? Uuid { get; set; }

   [JsonPropertyName("name")]
   public string? Name { get; set; }

   [JsonPropertyName("currency")]
   public string? Currency { get; set; }

   [JsonPropertyName("available_balance")]
   public AvailableBalance? AvailableBalance { get; set; }

   [JsonPropertyName("default")]
   public bool Default { get; set; }

   [JsonPropertyName("active")]
   public bool Active { get; set; }

   [JsonPropertyName("created_at")]
   public DateTime CreatedAt { get; set; }

   [JsonPropertyName("updated_at")]
   public DateTime UpdatedAt { get; set; }

   [JsonPropertyName("deleted_at")]
   public DateTime? DeletedAt { get; set; }

   [JsonPropertyName("type")]
   public string? Type { get; set; }

   [JsonPropertyName("ready")]
   public bool Ready { get; set; }

   [JsonPropertyName("hold")]
   public Hold? Hold { get; set; }
}

public class AvailableBalance
{
   [JsonPropertyName("value")]
   public string? Value { get; set; }

   [JsonPropertyName("currency")]
   public string? Currency { get; set; }
}

public class Hold
{
   [JsonPropertyName("value")]
   public string? Value { get; set; }

   [JsonPropertyName("currency")]
   public string? Currency { get; set; }
}

public class AccountsResponse
{
   [JsonPropertyName("accounts")]
   public List<Account> Accounts { get; set; } = new();

   [JsonPropertyName("has_next")]
   public bool HasNext { get; set; }

   [JsonPropertyName("cursor")]
   public string Cursor { get; set; } = string.Empty;
}

/// <summary>
/// Order Placement Models
/// </summary>
public class CoinbaseOrderRequest
{
    [JsonPropertyName("client_order_id")]
    public string ClientOrderId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty; // BUY or SELL

    [JsonPropertyName("order_configuration")]
    public OrderConfiguration OrderConfiguration { get; set; } = new();
}

public class OrderConfiguration
{
    [JsonPropertyName("market_market_ioc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MarketIoc? MarketIoc { get; set; }

    [JsonPropertyName("limit_limit_gtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LimitGtc? LimitGtc { get; set; }
}

public class MarketIoc
{
    [JsonPropertyName("quote_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QuoteSize { get; set; }

    [JsonPropertyName("base_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseSize { get; set; }
}

public class LimitGtc
{
    [JsonPropertyName("base_size")]
    public string BaseSize { get; set; } = string.Empty;

    [JsonPropertyName("limit_price")]
    public string LimitPrice { get; set; } = string.Empty;

    [JsonPropertyName("post_only")]
    public bool PostOnly { get; set; } = false;
}

public class CoinbaseCreateOrderResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("error_response")]
    public CoinbaseErrorResponse? ErrorResponse { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }
}

public class CoinbaseErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class CoinbaseOrderDetailsResponse
{
    [JsonPropertyName("order")]
    public CoinbaseOrder? Order { get; set; }
}

public class CoinbaseOrder
{
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("side")]
    public string? Side { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [JsonPropertyName("filled_size")]
    public string? FilledSize { get; set; }

    [JsonPropertyName("avg_filled_price")]
    public string? AvgFilledPrice { get; set; }
}
