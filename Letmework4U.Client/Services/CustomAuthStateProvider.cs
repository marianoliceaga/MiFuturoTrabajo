using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Letmework4U.Client.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ILocalStorageService _localStorage;
        private readonly JwtSecurityTokenHandler _jwtHandler;
        private const string AUTH_TOKEN_KEY = "authToken";
        private const string REFRESH_TOKEN_KEY = "refreshToken";

        public CustomAuthStateProvider(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
            _jwtHandler = new JwtSecurityTokenHandler();
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorage.GetItemAsync<string>(AUTH_TOKEN_KEY);

            if (string.IsNullOrWhiteSpace(token))
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            try
            {
                // Parse JWT token
                var jwtToken = _jwtHandler.ReadJwtToken(token);

                // Check if token is expired
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    // Token expired, clear storage
                    await ClearTokensAsync();
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                // Create claims identity from token
                var claims = jwtToken.Claims.ToList();
                var identity = new ClaimsIdentity(claims, "jwt");
                var user = new ClaimsPrincipal(identity);

                return new AuthenticationState(user);
            }
            catch
            {
                // Invalid token, clear storage
                await ClearTokensAsync();
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }

        public async Task MarkUserAsAuthenticated(string accessToken, string refreshToken)
        {
            await _localStorage.SetItemAsync(AUTH_TOKEN_KEY, accessToken);
            await _localStorage.SetItemAsync(REFRESH_TOKEN_KEY, refreshToken);

            var authState = await GetAuthenticationStateAsync();
            NotifyAuthenticationStateChanged(Task.FromResult(authState));
        }

        public async Task MarkUserAsLoggedOut()
        {
            await ClearTokensAsync();

            var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
            var authState = Task.FromResult(new AuthenticationState(anonymousUser));
            NotifyAuthenticationStateChanged(authState);
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            return await _localStorage.GetItemAsync<string>(AUTH_TOKEN_KEY);
        }

        public async Task<string?> GetRefreshTokenAsync()
        {
            return await _localStorage.GetItemAsync<string>(REFRESH_TOKEN_KEY);
        }

        public async Task UpdateTokensAsync(string accessToken, string refreshToken)
        {
            await _localStorage.SetItemAsync(AUTH_TOKEN_KEY, accessToken);
            await _localStorage.SetItemAsync(REFRESH_TOKEN_KEY, refreshToken);
        }

        private async Task ClearTokensAsync()
        {
            await _localStorage.RemoveItemAsync(AUTH_TOKEN_KEY);
            await _localStorage.RemoveItemAsync(REFRESH_TOKEN_KEY);
        }

        public async Task<bool> IsTokenExpiredAsync()
        {
            var token = await _localStorage.GetItemAsync<string>(AUTH_TOKEN_KEY);
            if (string.IsNullOrWhiteSpace(token)) return true;

            try
            {
                var jwtToken = _jwtHandler.ReadJwtToken(token);
                return jwtToken.ValidTo < DateTime.UtcNow;
            }
            catch
            {
                return true;
            }
        }
    }
}
