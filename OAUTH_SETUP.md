# OAuth Hybrid Authentication Setup Guide

## Overview

MiFuturoTrabajo.Api implements a hybrid authentication system that combines:
- **External OAuth Providers** (Google, Microsoft) for user login
- **Internal JWT Tokens** for API authentication

This approach allows users to login with their existing Google/Microsoft accounts while the API uses its own JWT tokens for authorization, eliminating the need for Microsoft Entra B2C or other external authentication infrastructure.

## Architecture

```
┌─────────────┐      OAuth Token       ┌──────────────┐
│   Browser   │ ──────────────────────> │   Google/    │
│   (Blazor)  │ <────────────────────── │  Microsoft   │
└─────────────┘      ID Token           └──────────────┘
       │
       │ POST /api/auth/oauth-login
       │ { provider, idToken }
       ▼
┌─────────────┐                        ┌──────────────┐
│  API Server │ ───── Validate ──────> │ OAuthService │
│             │ <──── User Info ────── │              │
└─────────────┘                        └──────────────┘
       │
       │ Find/Create User
       │ Generate Internal JWT
       ▼
┌─────────────┐
│  Client     │ ◄──── Access Token ────┐
│ (Stored JWT)│       Refresh Token     │
└─────────────┘                         │
       │                                │
       │ All subsequent API calls       │
       │ Authorization: Bearer <JWT>    │
       └────────────────────────────────┘
```

## Configuration

### 1. API Configuration (appsettings.json)

Add the following OAuth configuration section:

```json
{
  "OAuth": {
    "Google": {
      "ClientId": "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
    },
    "Microsoft": {
      "ClientId": "YOUR_MICROSOFT_CLIENT_ID",
      "TenantId": "common"
    }
  }
}
```

### 2. Google OAuth Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing one
3. Navigate to **APIs & Services** > **Credentials**
4. Click **Create Credentials** > **OAuth 2.0 Client ID**
5. Configure OAuth consent screen if needed
6. Application type: **Web application**
7. Add authorized JavaScript origins:
   - `https://localhost:5001` (local development)
   - `https://your-production-domain.com`
8. Add authorized redirect URIs (for client-side OAuth flow):
   - `https://localhost:5001/authentication/login-callback`
   - `https://your-production-domain.com/authentication/login-callback`
9. Copy the **Client ID** and add to `appsettings.json`

**Important**: You only need the Client ID. The client secret is NOT needed because token validation happens server-side using Google's public keys.

### 3. Microsoft OAuth Setup

1. Go to [Azure Portal](https://portal.azure.com/)
2. Navigate to **Microsoft Entra ID** (formerly Azure Active Directory)
3. Go to **App registrations** > **New registration**
4. Configure:
   - Name: "MiFuturoTrabajo Client"
   - Supported account types: **Accounts in any organizational directory (Any Microsoft Entra ID tenant - Multitenant) and personal Microsoft accounts**
   - Redirect URI: **Single-page application (SPA)** - `https://localhost:5001/authentication/login-callback`
5. After registration, copy the **Application (client) ID**
6. Under **Authentication**, add additional redirect URIs for production
7. Under **API permissions**, ensure these are added:
   - `User.Read` (Microsoft Graph)
   - `openid`
   - `profile`
   - `email`
8. Add the Client ID to `appsettings.json`

**TenantId Configuration**:
- Use `"common"` to allow both personal Microsoft accounts and work/school accounts
- Use specific tenant ID to restrict to a single organization

## Frontend Integration (Blazor)

### 1. Install OAuth Libraries

For Blazor WebAssembly, you'll need to add OAuth libraries:

```bash
# For Google OAuth
dotnet add package Microsoft.AspNetCore.Components.WebAssembly.Authentication

# You might also use a JavaScript library via JSInterop
```

### 2. Client-Side OAuth Flow

```csharp
// Example using Google Sign-In JavaScript library
// Add to index.html:
<script src="https://accounts.google.com/gsi/client" async defer></script>

// In your Login.razor component:
@inject HttpClient Http
@inject NavigationManager Navigation

<div id="g_id_onload"
     data-client_id="YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
     data-callback="handleGoogleResponse">
</div>

@code {
    [JSInvokable]
    public async Task HandleGoogleResponse(string idToken)
    {
        var request = new OAuthLoginRequest
        {
            Provider = OAuthProvider.Google,
            IdToken = idToken
        };

        var response = await Http.PostAsJsonAsync("/api/auth/oauth-login", request);

        if (response.IsSuccessStatusCode)
        {
            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
            // Store authResponse.AccessToken and RefreshToken
            // Redirect to dashboard
            Navigation.NavigateTo("/dashboard");
        }
    }
}
```

### 3. Microsoft OAuth Flow

For Microsoft, you can use MSAL.js:

```html
<!-- Add to index.html -->
<script src="https://alcdn.msauth.net/browser/2.30.0/js/msal-browser.min.js"></script>

<script>
const msalConfig = {
    auth: {
        clientId: 'YOUR_MICROSOFT_CLIENT_ID',
        authority: 'https://login.microsoftonline.com/common',
        redirectUri: window.location.origin + '/authentication/login-callback'
    }
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

async function loginWithMicrosoft() {
    const loginResponse = await msalInstance.loginPopup({
        scopes: ['openid', 'profile', 'email']
    });

    // Send the ID token to your API
    const idToken = loginResponse.idToken;

    // Call your API
    await fetch('/api/auth/oauth-login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            provider: 1, // Microsoft = 2, Google = 1
            idToken: idToken
        })
    });
}
</script>
```

## API Endpoints

### OAuth Login

**POST** `/api/auth/oauth-login`

Request:
```json
{
  "provider": 1,  // 1 = Google, 2 = Microsoft
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6..." // ID token from OAuth provider
}
```

Response (Success - 200 OK):
```json
{
  "userId": "abc123",
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",  // Internal JWT
  "refreshToken": "def456...",
  "tokenExpiry": "2025-01-02T14:30:00Z",
  "applicantId": "xyz789"
}
```

Response (Error - 401 Unauthorized):
```json
{
  "error": "Invalid OAuth token or provider"
}
```

## How It Works

### OAuth Login Flow

1. **Client-Side OAuth**: User clicks "Sign in with Google/Microsoft" button in Blazor app
2. **OAuth Provider Authentication**: OAuth provider (Google/Microsoft) handles authentication
3. **ID Token Received**: Client receives an ID token from the OAuth provider
4. **Token Sent to API**: Client sends the ID token to `/api/auth/oauth-login`
5. **Server-Side Validation**: API validates the ID token with the OAuth provider
6. **User Creation/Lookup**: API finds existing user by email or creates new user
7. **Internal JWT Generation**: API generates its own JWT access and refresh tokens
8. **Response**: Client receives internal JWT tokens
9. **Subsequent Requests**: Client uses internal JWT for all API calls

### Token Validation

#### Google Token Validation
- Uses `Google.Apis.Auth` library
- Validates token signature using Google's public keys
- Verifies audience matches configured Client ID
- Checks token expiration
- Extracts user info (email, name, picture, etc.)

#### Microsoft Token Validation
- Parses JWT token manually
- Validates audience claim
- Validates issuer (must be from login.microsoftonline.com)
- Checks token expiration
- Extracts user claims (email, name, oid, etc.)

### User Account Linking

The system supports users with multiple authentication methods:

1. **OAuth-Only User**: User created via OAuth, `PasswordHash` is `null`
2. **Password-Only User**: User created via email/password registration
3. **Hybrid User**: User can login with both OAuth and password
   - If OAuth user later sets a password, they can use both methods
   - If password user later uses OAuth with same email, OAuth info is added

## Security Considerations

### Token Security

- **ID Token Lifetime**: OAuth ID tokens are short-lived (typically 1 hour)
- **Internal JWT Lifetime**: Access tokens expire in 60 minutes, refresh tokens in 30 days for OAuth users
- **HTTPS Required**: All OAuth flows must use HTTPS in production
- **Token Storage**: Store JWT tokens securely (not in localStorage - use secure cookies or sessionStorage)

### Validation

- **Audience Validation**: Ensures tokens are intended for your application
- **Issuer Validation**: Ensures tokens come from legitimate OAuth providers
- **Expiration Checking**: Rejects expired tokens
- **Email Verification**: OAuth providers pre-verify emails, `EmailConfirmed` set to `true`

### Account Security

- **Account Linking**: Accounts are linked by email address
- **No Password Required**: OAuth-only users don't need passwords
- **Failed Login Attempts**: Account lockout doesn't apply to OAuth logins
- **Active Status**: Deactivated accounts cannot login via OAuth

## Testing

### Local Development

1. Configure OAuth providers as described above
2. Add `https://localhost:5001` to authorized origins/redirect URIs
3. Update `appsettings.json` with Client IDs
4. Run the API: `dotnet run --project MiFuturoTrabajo.Api`
5. Test OAuth login from Blazor client

### Testing with Postman

You can test the OAuth endpoint by:

1. Get a valid ID token from OAuth provider (use their OAuth playground)
2. Send POST request to `/api/auth/oauth-login`:
```json
{
  "provider": 1,
  "idToken": "PASTE_VALID_ID_TOKEN_HERE"
}
```
3. Verify you receive internal JWT tokens in response

## Troubleshooting

### "Invalid OAuth token or provider"

- Verify the ID token is not expired
- Check Client ID matches in appsettings.json and OAuth provider configuration
- Ensure token is for the correct audience

### "Account is locked"

- OAuth login respects account lockout status
- Check `User.IsLocked` and `User.LockoutEnd` in database

### "Account is deactivated"

- OAuth login respects active status
- Check `User.IsActive` in database

### CORS Errors

- Ensure Blazor client URL is configured in `BlazorClient:Url`
- Verify CORS policy in Program.cs allows your frontend origin

## Migration Notes

### Existing Users

If you already have users with email/password authentication:

- They can continue using email/password login
- If they login via OAuth with the same email, their account will be updated with OAuth info
- They can then use either method to login

### Database Schema

The following fields were added to the `User` entity:

```csharp
public string? OAuthProvider { get; set; }      // "Google" or "Microsoft"
public string? OAuthProviderId { get; set; }    // Provider's unique user ID
public string? ProfilePictureUrl { get; set; }  // From OAuth profile
public string? PasswordHash { get; set; }       // Now nullable
```

No migration required for Cosmos DB - fields are automatically added when users login.

## Production Deployment

### Azure App Service

1. Add OAuth Client IDs to **Application Settings**:
   - `OAuth__Google__ClientId`
   - `OAuth__Microsoft__ClientId`
   - `OAuth__Microsoft__TenantId`

2. Update OAuth provider redirect URIs to production domain

3. Ensure HTTPS is enforced

4. Update CORS policy with production Blazor URL

### Environment Variables

```bash
OAuth__Google__ClientId=YOUR_GOOGLE_CLIENT_ID
OAuth__Microsoft__ClientId=YOUR_MICROSOFT_CLIENT_ID
OAuth__Microsoft__TenantId=common
```

## Additional Resources

- [Google OAuth Documentation](https://developers.google.com/identity/protocols/oauth2)
- [Microsoft Identity Platform](https://learn.microsoft.com/en-us/entra/identity-platform/)
- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)

## Support

For issues or questions:
- Check application logs for detailed error messages
- Review OAuth provider documentation
- Verify all configuration values match between client and server
