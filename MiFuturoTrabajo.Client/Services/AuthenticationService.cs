using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace MiFuturoTrabajo.Client.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly HttpClient _httpClient;
        private readonly CustomAuthStateProvider _authStateProvider;

        public AuthenticationService(
            HttpClient httpClient,
            CustomAuthStateProvider authStateProvider)
        {
            _httpClient = httpClient;
            _authStateProvider = authStateProvider;
        }

        public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/register", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (authResponse != null)
                    {
                        await _authStateProvider.MarkUserAsAuthenticated(
                            authResponse.AccessToken,
                            authResponse.RefreshToken);
                        return authResponse;
                    }
                }

                var errorContent = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                if (errorContent.TryGetProperty("error", out var errorProp))
                {
                    throw new Exception(errorProp.GetString());
                }

                throw new Exception("Registration failed for an unknown reason.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Registration error: {ex.Message}");
                throw; // Rethrow to be caught by the UI
            }
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/login", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (authResponse != null)
                    {
                        await _authStateProvider.MarkUserAsAuthenticated(
                            authResponse.AccessToken,
                            authResponse.RefreshToken);
                        return authResponse;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
                return null;
            }
        }

        public async Task<AuthResponse?> OAuthLoginAsync(OAuthLoginRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/oauth-login", request);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (authResponse != null)
                    {
                        await _authStateProvider.MarkUserAsAuthenticated(
                            authResponse.AccessToken,
                            authResponse.RefreshToken);
                        return authResponse;
                    }
                }

                var errorContent = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                if (errorContent.TryGetProperty("error", out var errorProp))
                {
                    throw new Exception(errorProp.GetString());
                }

                throw new Exception("OAuth login failed for an unknown reason.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OAuth login error: {ex.Message}");
                throw; // Rethrow to be caught by the UI
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                // Call backend logout endpoint to revoke refresh token
                var token = await _authStateProvider.GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                    // Best effort - don't fail if backend call fails
                    await _httpClient.PostAsync("auth/logout", null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backend logout error: {ex.Message}");
            }
            finally
            {
                // Always clear local state regardless of backend call success
                await _authStateProvider.MarkUserAsLoggedOut();
            }
        }

        public async Task<AuthResponse?> RefreshTokenAsync()
        {
            try
            {
                var accessToken = await _authStateProvider.GetAccessTokenAsync();
                var refreshToken = await _authStateProvider.GetRefreshTokenAsync();

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                {
                    return null;
                }

                var refreshRequest = new RefreshTokenRequest
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                };

                var response = await _httpClient.PostAsJsonAsync("auth/refresh", refreshRequest);

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (authResponse != null)
                    {
                        await _authStateProvider.UpdateTokensAsync(
                            authResponse.AccessToken,
                            authResponse.RefreshToken);
                        return authResponse;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token refresh error: {ex.Message}");
                return null;
            }
        }

        public async Task<UserDto?> GetCurrentUserAsync()
        {
            try
            {
                var token = await _authStateProvider.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync("auth/me");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<UserDto>();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get current user error: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SendEmailVerificationAsync()
        {
            try
            {
                var token = await _authStateProvider.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsync("auth/send-verification", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send email verification error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> VerifyEmailAsync(string email, string token)
        {
            try
            {
                var request = new VerifyEmailRequest
                {
                    Email = email,
                    VerificationToken = token
                };

                var response = await _httpClient.PostAsJsonAsync("auth/verify-email", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Verify email error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ForgotPasswordAsync(string email)
        {
            try
            {
                var request = new ForgotPasswordRequest { Email = email };
                var response = await _httpClient.PostAsJsonAsync("auth/forgot-password", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Forgot password error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/reset-password", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reset password error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ChangePasswordAsync(ChangePasswordRequest request)
        {
            try
            {
                var token = await _authStateProvider.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsJsonAsync("auth/change-password", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Change password error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            return authState.User.Identity?.IsAuthenticated ?? false;
        }
    }
}
