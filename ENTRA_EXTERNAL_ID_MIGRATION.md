# Migration from Azure AD B2C to Microsoft Entra External ID

## Overview

This document summarizes the migration from Azure AD B2C to Microsoft Entra External ID (formerly known as Azure AD B2C for customers).

## What Changed

### 1. Configuration Property Names

**Before (Azure AD B2C):**
```json
{
  "AzureAdB2C": {
    "Authority": "https://YOUR_TENANT.b2clogin.com/YOUR_TENANT.onmicrosoft.com/B2C_1_SignUpSignIn",
    "ClientId": "YOUR_BLAZOR_CLIENT_ID",
    "ValidateAuthority": false
  }
}
```

**After (Microsoft Entra External ID):**
```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://YOUR_TENANT.ciamlogin.com/YOUR_TENANT.onmicrosoft.com",
    "ClientId": "YOUR_BLAZOR_CLIENT_ID",
    "ValidateAuthority": false
  }
}
```

### 2. Key Differences

| Aspect | Azure AD B2C | Microsoft Entra External ID |
|--------|--------------|----------------------------|
| **Configuration Key** | `AzureAdB2C` | `MicrosoftEntraExternalId` |
| **Authority Domain** | `.b2clogin.com` | `.ciamlogin.com` |
| **User Flow Naming** | `B2C_1_SignUpSignIn` | `susi` (sign-up/sign-in) |
| **API Scope Format** | `https://YOUR_TENANT.onmicrosoft.com/api/user_impersonation` | `api://YOUR_API_CLIENT_ID/user_impersonation` |

### 3. Files Modified

#### Configuration Files:
- ✅ [Letmework4U.Client/wwwroot/appsettings.json](d:\Projects\Letmework4U\Letmework4U.Client\wwwroot\appsettings.json)
- ✅ [Letmework4U.Client/wwwroot/appsettings.Development.json](d:\Projects\Letmework4U\Letmework4U.Client\wwwroot\appsettings.Development.json)

#### Code Files:
- ✅ [Letmework4U.Client/Program.cs](d:\Projects\Letmework4U\Letmework4U.Client\Program.cs:18) - Updated configuration binding
- ✅ [Letmework4U.Functions/Helpers/AuthHelper.cs](d:\Projects\Letmework4U\Letmework4U.Functions\Helpers\AuthHelper.cs:17) - Updated comments
- ✅ [Letmework4U.Infrastructure/Entities/Applicant.cs](d:\Projects\Letmework4U\Letmework4U.Infrastructure\Entities\Applicant.cs:8) - Updated comments

#### Documentation Files:
- ✅ [MSAL_AUTHENTICATION_SETUP.md](d:\Projects\Letmework4U\MSAL_AUTHENTICATION_SETUP.md) - Complete rewrite
- ✅ [IMPLEMENTATION_GUIDE.md](d:\Projects\Letmework4U\IMPLEMENTATION_GUIDE.md) - Updated references
- ✅ [README.md](d:\Projects\Letmework4U\README.md) - Updated references

## What You Need to Do

### 1. Create Microsoft Entra External ID Tenant

Instead of creating an Azure AD B2C tenant, you'll create a Microsoft Entra External ID tenant:

1. Go to [Azure Portal](https://portal.azure.com)
2. Search for "Microsoft Entra External ID"
3. Create a new External ID tenant
4. Note your tenant name (e.g., `mycompany`)

### 2. Register Applications

**Blazor Client App Registration:**
1. In your External ID tenant, go to **App registrations**
2. Create new registration for Blazor app
3. Set redirect URIs:
   - `https://localhost:5001/authentication/login-callback`
   - `https://YOUR_DOMAIN/authentication/login-callback`
4. Enable **Implicit grant**: ID tokens
5. Copy the **Application (client) ID**

**Azure Functions API Registration:**
1. Create another app registration for your API
2. Go to **Expose an API**
3. Add scope: `user_impersonation`
4. Copy the **Application (client) ID**

### 3. Update Configuration Files

Update `appsettings.json` and `appsettings.Development.json`:

```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://YOURTENANT.ciamlogin.com/YOURTENANT.onmicrosoft.com",
    "ClientId": "YOUR_BLAZOR_CLIENT_ID_HERE",
    "ValidateAuthority": false
  },
  "ApiBaseUrl": "http://localhost:7071/api/",
  "ApiScopes": [
    "api://YOUR_API_CLIENT_ID_HERE/user_impersonation"
  ]
}
```

Replace:
- `YOURTENANT` - Your External ID tenant name
- `YOUR_BLAZOR_CLIENT_ID_HERE` - Client ID from Blazor app registration
- `YOUR_API_CLIENT_ID_HERE` - Client ID from API app registration

### 4. Configure Azure Functions Authentication

In your Azure Functions `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__ConnectionString": "YOUR_COSMOS_DB_CONNECTION_STRING",
    "CosmosDb__DatabaseName": "Letmework4U",
    "MicrosoftEntraExternalId__Instance": "https://YOUR_TENANT.ciamlogin.com/",
    "MicrosoftEntraExternalId__TenantId": "YOUR_TENANT_ID",
    "MicrosoftEntraExternalId__ClientId": "YOUR_API_CLIENT_ID",
    "MicrosoftEntraExternalId__Audience": "api://YOUR_API_CLIENT_ID",
    "AzureCommunicationServices__ConnectionString": "YOUR_ACS_CONNECTION_STRING",
    "AzureCommunicationServices__SenderAddress": "DoNotReply@YOUR_DOMAIN.com"
  }
}
```

**Important Notes:**
- Use double underscores (`__`) in environment variables for nested configuration
- The `Instance` should end with a trailing slash
- `TenantId` is your External ID tenant's GUID
- `ClientId` is the Application ID from your API app registration
- `Audience` should match the Application ID URI (typically `api://YOUR_API_CLIENT_ID`)

## Benefits of Microsoft Entra External ID

1. **Modern Platform**: Latest Microsoft identity platform features
2. **Better Developer Experience**: Simplified configuration
3. **Enhanced Security**: Built on Microsoft Entra ID foundation
4. **Unified Management**: Single portal for all identity scenarios
5. **Future-Proof**: Microsoft's strategic direction for customer identity

## Authentication Flow (Unchanged)

The authentication flow remains the same:
1. User visits app → Redirected to login if not authenticated
2. MSAL redirects to Microsoft Entra External ID
3. User authenticates
4. Token returned to app
5. Token automatically attached to API calls
6. API validates token and extracts user claims

## Backward Compatibility

⚠️ **Important**: This is a breaking change. You cannot use the same tenant/configuration for both Azure AD B2C and Microsoft Entra External ID. You must:
- Create a new External ID tenant
- Re-register applications
- Update all configuration files
- Migrate users (if needed)

## Testing

After migration:
1. Start the Blazor app: `dotnet run --project Letmework4U.Client`
2. Start Azure Functions: `func start` (in Functions directory)
3. Navigate to the app
4. Click "Log in" - should redirect to External ID login
5. Sign up/Sign in
6. Verify token is attached to API calls
7. Test CRUD operations

## Support

If you encounter issues:
- Check [Microsoft Entra External ID documentation](https://learn.microsoft.com/entra/external-id/)
- Review [MSAL_AUTHENTICATION_SETUP.md](MSAL_AUTHENTICATION_SETUP.md) for detailed setup
- Ensure all Client IDs and Authority URLs are correct
- Verify API scopes match between app registrations and configuration

## Migration Checklist

- [ ] Create Microsoft Entra External ID tenant
- [ ] Register Blazor client application
- [ ] Register Azure Functions API application
- [ ] Configure API scopes
- [ ] Update appsettings.json
- [ ] Update appsettings.Development.json
- [ ] Update local.settings.json (Functions)
- [ ] Test login flow
- [ ] Test API authentication
- [ ] Test token refresh
- [ ] Deploy to Azure (if applicable)
- [ ] Update production configuration
