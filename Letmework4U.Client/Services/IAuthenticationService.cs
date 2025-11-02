using Letmework4U.Shared.Models;

namespace Letmework4U.Client.Services
{
    public interface IAuthenticationService
    {
        Task<AuthResponse?> RegisterAsync(RegisterRequest request);
        Task<AuthResponse?> LoginAsync(LoginRequest request);
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
