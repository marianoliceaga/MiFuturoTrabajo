using Microsoft.Azure.Functions.Worker.Http;
using System.Security.Claims;

namespace Letmework4U.Functions.Helpers
{
    public static class AuthHelper
    {
        public static string? GetUserIdFromRequest(HttpRequestData req)
        {
            // Try to get the user ID from the claims principal
            var principal = req.Identities?.FirstOrDefault();
            if (principal == null)
            {
                return null;
            }

            // Azure AD B2C stores the user ID in different claim types
            // Try multiple claim types to ensure compatibility
            var userId = principal.Claims?.FirstOrDefault(c =>
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == "sub" ||
                c.Type == "oid" ||
                c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

            return userId;
        }

        public static bool IsAuthenticated(HttpRequestData req)
        {
            var userId = GetUserIdFromRequest(req);
            return !string.IsNullOrEmpty(userId);
        }
    }
}
