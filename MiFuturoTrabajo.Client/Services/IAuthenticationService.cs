using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public interface IAuthenticationService
    {
        Task<AuthResponse?> RegisterAsync(RegisterRequest request);
        Task<AuthResponse?> LoginAsync(LoginRequest request);
        Task<AuthResponse?> OAuthLoginAsync(OAuthLoginRequest request);
        Task LogoutAsync();
        Task<AuthResponse?> RefreshTokenAsync();
        Task<UserDto?> GetCurrentUserAsync();
        Task<bool> SendEmailVerificationAsync();
        Task<bool> VerifyEmailAsync(string token);
        Task<bool> ForgotPasswordAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
        Task<bool> ChangePasswordAsync(ChangePasswordRequest request);
        Task<bool> IsAuthenticatedAsync();
    }
}
