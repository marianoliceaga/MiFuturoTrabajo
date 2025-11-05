// AuthenticationHttpMessageHandler.cs
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Letmework4U.Client.Services
{
    public class AuthenticationHttpMessageHandler : DelegatingHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        // Inject IHttpClientFactory, NOT HttpClient
        public AuthenticationHttpMessageHandler(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Example: Get a token
            // ❗ Use the "TokenClient", NOT the client being built.
            var tokenClient = _httpClientFactory.CreateClient("TokenClient");

            // var token = await tokenClient.GetStringAsync("my-token-endpoint");
            // request.Headers.Authorization = new ("Bearer", token);

            // Continue the request pipeline
            return await base.SendAsync(request, cancellationToken);
        }
    }
}