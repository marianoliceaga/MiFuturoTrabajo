using System.Security.Claims;

namespace MiFuturoTrabajo.Api.Helpers
{
    public static class AuthHelper
    {
        public static string? GetUserIdFromClaims(ClaimsPrincipal? user)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                return null;
            }

            // Try multiple claim types to ensure compatibility
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst("oid")?.Value
                ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

            return userId;
        }

        public static bool IsAuthenticated(ClaimsPrincipal? user)
        {
            var userId = GetUserIdFromClaims(user);
            return !string.IsNullOrEmpty(userId);
        }
    }
}
