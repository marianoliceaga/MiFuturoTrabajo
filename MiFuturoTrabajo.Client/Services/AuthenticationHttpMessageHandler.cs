// AuthenticationHttpMessageHandler.cs
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Blazored.LocalStorage;

namespace MiFuturoTrabajo.Client.Services
{
    public class AuthenticationHttpMessageHandler : DelegatingHandler
    {
        private readonly ILocalStorageService _localStorage;

        public AuthenticationHttpMessageHandler(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Get the access token from local storage (key must match CustomAuthStateProvider)
            var token = await _localStorage.GetItemAsync<string>("authToken");

            // If token exists, add it to the Authorization header
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // Continue the request pipeline
            return await base.SendAsync(request, cancellationToken);
        }
    }
}