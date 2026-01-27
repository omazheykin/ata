using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

// Replace these with the keys the user claims to have generated
// For security, we'll ask the user to input them or set them as env vars, 
// but for this test script we'll use placeholders that the user can replace
var apiKey = "ah8h8mQSn2OIkOzd8gd5WZMyZA3MITNVL76ur9BATukHm1RywaDFEaT1gcIRQtm0";
var apiSecret = "I1FlFzhWR0IS5uJu11HNnlpqL4kEWNfWBjceXp1joelACi9w7Qv72rlvix3YnQW1";

var client = new HttpClient { BaseAddress = new Uri("https://api.binance.com") };

Console.WriteLine("Testing Binance Authentication...");

try
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var query = $"timestamp={timestamp}";
    var signature = Sign(query, apiSecret);

    Console.WriteLine($"Timestamp: {timestamp}");
    Console.WriteLine($"Query: {query}");
    Console.WriteLine($"Signature: {signature}");

    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/account?{query}&signature={signature}");
    request.Headers.Add("X-MBX-APIKEY", apiKey);

    var response = await client.SendAsync(request);
    
    Console.WriteLine($"Status Code: {response.StatusCode}");
    var content = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Response: {content}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex}");
}

string Sign(string query, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(query));
    return BitConverter.ToString(hash).Replace("-", "").ToLower();
}
