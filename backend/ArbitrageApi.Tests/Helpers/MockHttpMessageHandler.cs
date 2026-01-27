using System.Net;
using System.Text;
using System.Text.Json;

namespace ArbitrageApi.Tests.Helpers;

/// <summary>
/// Mock HTTP message handler for testing HTTP requests without hitting real APIs
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responseFactory(request));
    }

    /// <summary>
    /// Creates a handler that returns a successful JSON response
    /// </summary>
    public static MockHttpMessageHandler CreateWithJsonResponse<T>(T responseObject, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new MockHttpMessageHandler(request =>
        {
            var json = JsonSerializer.Serialize(responseObject);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
    }

    /// <summary>
    /// Creates a handler that returns an error response
    /// </summary>
    public static MockHttpMessageHandler CreateWithError(HttpStatusCode statusCode, string errorMessage = "")
    {
        return new MockHttpMessageHandler(request =>
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(errorMessage, Encoding.UTF8, "application/json")
            };
        });
    }
}
