using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

// Sandbox Keys (from user)
var apiKey = "cb5e8bc2-6d10-4fba-a376-9281f6cfd336";
var apiSecret = "3uF5aPPJ1owVO7nanhnd4aTjER+B/HkpTt59p8HvgVN0JuylQ/KzRgqL07uItk/5KLD4WGDO3PqHWmV7yFc0SA==";
var baseUrl = "https://api-public.sandbox.exchange.coinbase.com";

var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
// client.DefaultRequestHeaders.Add("User-Agent", "CoinbaseTest");

Console.WriteLine("Testing Coinbase Sandbox...");

// 1. Test Fees (Authenticated)
try
{
    Console.WriteLine("\nFetching Fees...");
    var requestPath = "/fees";
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    var method = "GET";
    var body = "";
    var signature = Sign(timestamp, method, requestPath, body, apiSecret);

    using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
    request.Headers.Add("CB-ACCESS-KEY", apiKey);
    request.Headers.Add("CB-ACCESS-SIGN", signature);
    request.Headers.Add("CB-ACCESS-TIMESTAMP", timestamp);
    request.Headers.Add("CB-ACCESS-PASSPHRASE", "sandbox"); // Try adding passphrase if needed, but usually keys have one. 
    // Wait, Sandbox keys from public.sandbox.pro usually have a passphrase. The user didn't provide one.
    // If it fails, I'll ask the user. But let's try without first.

    var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    
    Console.WriteLine($"Status: {response.StatusCode}");
    Console.WriteLine($"Response: {content}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error fetching fees: {ex.Message}");
}

// 2. Test Ticker (Public/Exchange API)
try
{
    Console.WriteLine("\nFetching Ticker (LINK-USD)...");
    var ticker = await client.GetFromJsonAsync<TickerResponse>("/products/LINK-USD/ticker");
    
    Console.WriteLine($"Status: OK");
    Console.WriteLine($"Price: {ticker?.Price}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error fetching ticker: {ex.Message}");
}

string Sign(string timestamp, string method, string requestPath, string body, string secret)
{
    var message = timestamp + method + requestPath + body;
    var secretBytes = Convert.FromBase64String(secret); // Sandbox secret is usually Base64
    using var hmac = new HMACSHA256(secretBytes);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    return Convert.ToBase64String(hash);
}

class TickerResponse
{
    [JsonPropertyName("price")]
    public string Price { get; set; }
}
