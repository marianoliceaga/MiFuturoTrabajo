using System.Net;
using System.Net.Http.Headers;

namespace Letmework4U.Client.Services
{
    public class AuthenticationHttpMessageHandler : DelegatingHandler
    {
        private readonly CustomAuthStateProvider _authStateProvider;
        private readonly IAuthenticationService _authenticationService;

        public AuthenticationHttpMessageHandler(
            CustomAuthStateProvider authStateProvider,
            IAuthenticationService authenticationService)
        {
            _authStateProvider = authStateProvider;
            _authenticationService = authenticationService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Skip auth endpoints
            if (request.RequestUri?.AbsolutePath.Contains("/auth/") == true &&
                !request.RequestUri.AbsolutePath.Contains("/auth/me") &&
                !request.RequestUri.AbsolutePath.Contains("/auth/send-verification") &&
                !request.RequestUri.AbsolutePath.Contains("/auth/change-password"))
            {
                return await base.SendAsync(request, cancellationToken);
            }

            // Get access token
            var token = await _authStateProvider.GetAccessTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                // Check if token is expired
                if (await _authStateProvider.IsTokenExpiredAsync())
                {
                    // Attempt to refresh token
                    var refreshResult = await _authenticationService.RefreshTokenAsync();
                    if (refreshResult != null)
                    {
                        token = refreshResult.AccessToken;
                    }
                    else
                    {
                        // Refresh failed, user needs to login again
                        await _authStateProvider.MarkUserAsLoggedOut();
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    }
                }

                // Attach token to request
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // Send request
            var response = await base.SendAsync(request, cancellationToken);

            // Handle 401 Unauthorized
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Try to refresh token once
                var refreshResult = await _authenticationService.RefreshTokenAsync();
                if (refreshResult != null)
                {
                    // Retry request with new token
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshResult.AccessToken);
                    response = await base.SendAsync(request, cancellationToken);
                }
                else
                {
                    // Refresh failed, mark user as logged out
                    await _authStateProvider.MarkUserAsLoggedOut();
                }
            }

            return response;
        }
    }
}
