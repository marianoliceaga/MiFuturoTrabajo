# Authentication Setup Guide - Letmework4U

This guide covers setting up multiple identity providers for Microsoft Entra External ID.

## Current Configuration

- **Tenant**: letmework4u.onmicrosoft.com
- **Authority**: https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com
- **Client App ID**: 37b37fa2-c0c4-4766-8bc7-1478524d359d
- **API App ID**: f0788507-bdab-4aa8-bf96-857dda053319

## Supported Identity Providers

Configure these providers to allow different types of user sign-ins:

### 1. Microsoft Account (Personal Accounts)
Allows: @outlook.com, @hotmail.com, @live.com

**Setup in Azure Portal:**
1. Microsoft Entra External ID → Identity providers
2. Click "+ Microsoft Account"
3. Follow the wizard to enable
4. Add to your user flow

### 2. Google Accounts

**Prerequisites - Google Cloud Console:**
1. Create project at https://console.cloud.google.com
2. Enable Google+ API
3. Configure OAuth consent screen (External)
4. Create OAuth 2.0 Client ID (Web application)
5. Add authorized redirect URI:
   ```
   https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp
   ```
6. Save Client ID and Client Secret

**Setup in Azure Portal:**
1. Microsoft Entra External ID → Identity providers
2. Click "+ Google"
3. Enter:
   - Client ID: [from Google Cloud Console]
   - Client secret: [from Google Cloud Console]
4. Click "Save"
5. Add to your user flow

### 3. Email + Password (Local Accounts)
Allows users to sign up with any email address

**Setup in Azure Portal:**
1. Microsoft Entra External ID → User flows
2. Select your sign-up/sign-in flow
3. Click "Identity providers"
4. Check "Email signup"
5. Save

### 4. Other Providers (Optional)

- **Facebook**: Requires Facebook App ID and App Secret
- **Apple**: Requires Apple Developer Account
- **Email OTP**: One-time passcode sent to email (no password)

## User Flow Configuration

After adding identity providers, configure your user flow:

1. **Navigate to**: Microsoft Entra External ID → User flows
2. **Select**: Your sign-up and sign-in flow
3. **Click**: Identity providers
4. **Enable**:
   - ✅ Microsoft Account
   - ✅ Google
   - ✅ Email signup (optional)
5. **Save** changes

## Testing Authentication

### Local Development Testing
```bash
# Start the Blazor client
cd Letmework4U.Client
dotnet run

# Navigate to: https://localhost:5001
# Click sign-in and verify identity provider options appear
```

### Expected Sign-In Screen
After configuration, users should see options to sign in with:
- Microsoft Account
- Google
- Email (if enabled)

## Troubleshooting

### "You can't sign in here with a personal account"
- **Cause**: Microsoft Account provider not enabled
- **Fix**: Add Microsoft Account identity provider and update user flow

### "Redirect URI mismatch" (Google)
- **Cause**: Google redirect URI doesn't match Azure configuration
- **Fix**: Verify redirect URI in Google Cloud Console matches:
  ```
  https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp
  ```

### Users see only organizational accounts
- **Cause**: App registration signInAudience is set to AzureADMyOrg
- **Fix**: Change to AzureADandPersonalMicrosoftAccount or use External ID properly

## Security Best Practices

1. **Never commit secrets** - Keep Client IDs in appsettings.json, secrets in Azure Key Vault
2. **Use separate Google projects** for dev/staging/production
3. **Limit redirect URIs** to only necessary domains
4. **Enable MFA** in Entra ID conditional access policies
5. **Monitor sign-ins** in Azure Portal → Sign-in logs

## Current Code Configuration

The Blazor client is already configured for MSAL authentication:

**File**: `Letmework4U.Client/Program.cs`
- MSAL authentication configured
- API scopes: `api://f0788507-bdab-4aa8-bf96-857dda053319/user_impersonation`
- AuthorizationMessageHandler for automatic token attachment

**File**: `Letmework4U.Client/wwwroot/appsettings.Development.json`
```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com",
    "ClientId": "37b37fa2-c0c4-4766-8bc7-1478524d359d",
    "ValidateAuthority": false
  },
  "ApiBaseUrl": "http://localhost:7071/api/",
  "ApiScopes": [
    "api://f0788507-bdab-4aa8-bf96-857dda053319/user_impersonation"
  ]
}
```

## Next Steps

1. ✅ Add Microsoft Account identity provider in Azure Portal
2. ✅ Set up Google OAuth credentials in Google Cloud Console
3. ✅ Add Google identity provider in Azure Portal
4. ✅ Update user flow to include both providers
5. ✅ Test sign-in with personal Microsoft account
6. ✅ Test sign-in with Google account
7. ✅ Deploy and test in production

## References

- [Microsoft Entra External ID Documentation](https://learn.microsoft.com/entra/external-id/)
- [Google OAuth 2.0 Setup](https://developers.google.com/identity/protocols/oauth2)
- [MSAL.js Documentation](https://learn.microsoft.com/azure/active-directory/develop/msal-overview)
