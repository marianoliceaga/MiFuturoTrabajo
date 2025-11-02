# Identity Providers Manual Setup Guide

This guide provides step-by-step instructions for configuring all identity providers for the Letmework4U application using Microsoft Entra External ID.

## Overview

We will configure the following identity providers:
1. ✅ **Microsoft Account** - Personal Microsoft accounts (@outlook.com, @hotmail.com, @live.com)
2. ✅ **Google** - Google accounts (@gmail.com and Google Workspace)
3. ✅ **Email One-Time Passcode (OTP)** - Sign in with any email using a verification code
4. ✅ **Microsoft Entra ID** - Work/school accounts from other organizations (optional)

## Prerequisites

- Access to Azure Portal: https://portal.azure.com
- Administrator access to the Entra External ID tenant: `letmework4u.onmicrosoft.com`
- For Google: Access to Google Cloud Console: https://console.cloud.google.com

---

## Part 1: Google OAuth Setup (Do This First)

### Step 1: Create Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Click the project dropdown at the top
3. Click **"New Project"**
4. Enter project details:
   - **Project name**: `Letmework4U` or `MiMejorMomento`
   - **Organization**: Leave as default
5. Click **"Create"**
6. Wait for the project to be created, then select it

### Step 2: Enable Required APIs

1. In the left menu, go to **"APIs & Services"** → **"Library"**
2. Search for **"Google+ API"** or **"Google Identity Services API"**
3. Click on it
4. Click **"Enable"**

### Step 3: Configure OAuth Consent Screen

1. Go to **"APIs & Services"** → **"OAuth consent screen"**
2. Select **"External"** user type
3. Click **"Create"**
4. Fill in the required information:

   **App information:**
   - App name: `MiMejorMomento` or `Letmework4U`
   - User support email: `[your email address]`
   - App logo: (optional)

   **App domain:**
   - Application home page: `https://letmework4u.azurestaticapps.net` (or your domain)
   - Application privacy policy link: (optional)
   - Application terms of service link: (optional)

   **Authorized domains:**
   - Add: `ciamlogin.com`
   - Add: `azurestaticapps.net` (if using Azure Static Web Apps)

   **Developer contact information:**
   - Email addresses: `[your email address]`

5. Click **"Save and Continue"**
6. **Scopes**: Click **"Save and Continue"** (no additional scopes needed)
7. **Test users**: Add your email for testing (optional)
8. Click **"Save and Continue"**
9. Review and click **"Back to Dashboard"**

### Step 4: Create OAuth 2.0 Client ID

1. Go to **"APIs & Services"** → **"Credentials"**
2. Click **"+ Create Credentials"** → **"OAuth client ID"**
3. If prompted to configure consent screen, complete Step 3 first
4. Configure the client:

   **Application type:** Web application

   **Name:** `Letmework4U - Entra External ID Integration`

   **Authorized JavaScript origins:** (leave empty)

   **Authorized redirect URIs:** Click **"+ Add URI"** and add:
   ```
   https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp
   ```

5. Click **"Create"**

### Step 5: Save Credentials

A dialog will appear with your credentials:

```
Client ID: [something like: 123456789-abcdefg.apps.googleusercontent.com]
Client Secret: [something like: GOCSPX-abcd1234efgh5678]
```

**IMPORTANT**: Copy and save both values immediately. You'll need them in Part 2.

---

## Part 2: Azure Portal Configuration

### Step 1: Access Microsoft Entra External ID

1. Go to [Azure Portal](https://portal.azure.com)
2. Sign in with your account
3. In the search bar at the top, type **"Microsoft Entra External ID"**
4. Click on **"Microsoft Entra External ID"** in the results
5. Or navigate directly: [External Identities](https://portal.azure.com/#view/Microsoft_AAD_IAM/CompanyRelationshipsMenuBlade/~/IdentityProviders)

### Step 2: Add Microsoft Account Provider

1. In the left menu, click **"Identity providers"** (under External Identities)
2. Click **"+ Microsoft Account"** in the top menu
3. Follow the setup wizard:
   - This provider is pre-configured by Microsoft
   - Click **"Save"**
4. You should see **"Microsoft Account"** in the list of identity providers

### Step 3: Add Google Provider

1. Still in **"Identity providers"**, click **"+ Google"** in the top menu
2. Enter the credentials from Part 1, Step 5:
   - **Client ID**: Paste the Google Client ID
   - **Client secret**: Paste the Google Client Secret
3. Click **"Save"**
4. You should see **"Google"** in the list of identity providers

### Step 4: Configure Email One-Time Passcode

1. In **"Identity providers"**, look for **"Email one-time passcode"**
2. If it's not visible, click **"All identity providers"**
3. Look for **"Email One-Time Passcode"** or similar
4. If it's already enabled, you're done!
5. If not:
   - Click on it
   - Toggle **"Enabled"** to **On**
   - Click **"Save"**

### Step 5: (Optional) Add Microsoft Entra ID

If you want to allow users from other organizations (work/school accounts):

1. In **"Identity providers"**, click **"+ Identity provider"**
2. Select **"Microsoft Entra ID"**
3. Choose configuration:
   - **Specific tenant**: For a specific organization
   - **Any Microsoft Entra ID tenant**: For any organization
4. Follow the wizard and click **"Save"**

---

## Part 3: Configure User Flow

### Step 1: Find Your User Flow

1. In Microsoft Entra External ID, click **"User flows"** in the left menu
2. You should see your existing user flow (e.g., "susi" or "signup_signin")
3. Click on the user flow name

### Step 2: Add Identity Providers to User Flow

1. In the user flow settings, click **"Identity providers"** in the left menu
2. You'll see a list of available identity providers
3. Check the boxes for:
   - ✅ **Microsoft Account**
   - ✅ **Google**
   - ✅ **Email signup** (if you want email/password local accounts)
   - ✅ **Email one-time passcode** (if available)
4. Click **"Save"** at the top

### Step 3: Configure Application Claims (Optional)

1. In the user flow, click **"Application claims"**
2. Ensure these claims are selected:
   - ✅ Email Addresses
   - ✅ Display Name
   - ✅ Given Name
   - ✅ Surname
   - ✅ User's Object ID
3. Click **"Save"**

---

## Part 4: Update App Registration (If Needed)

### Step 1: Check Client App Configuration

1. In Azure Portal, go to **"Microsoft Entra ID"** (or **"Entra External ID"**)
2. Click **"App registrations"** in the left menu
3. Click **"All applications"**
4. Find your client app: **ClientId: 37b37fa2-c0c4-4766-8bc7-1478524d359d**
5. Click on it

### Step 2: Verify Authentication Settings

1. Click **"Authentication"** in the left menu
2. Under **"Platform configurations"**, verify:
   - **Platform**: Single-page application
   - **Redirect URIs**:
     ```
     https://localhost:5001/authentication/login-callback
     https://[your-production-domain]/authentication/login-callback
     ```
3. Under **"Supported account types"**, ensure it's set to:
   - **"Accounts in any identity provider or organizational directory (for authenticating users with user flows)"**
4. Click **"Save"** if you made changes

### Step 3: Verify API Permissions

1. Click **"API permissions"** in the left menu
2. You should see:
   - **Microsoft Graph** → User.Read (delegated)
   - **Your API (f0788507-bdab-4aa8-bf96-857dda053319)** → user_impersonation
3. If permissions need admin consent, click **"Grant admin consent"**

---

## Part 5: Test the Configuration

### Step 1: Run the Application Locally

```bash
# Navigate to the Blazor client
cd Letmework4U.Client

# Run the application
dotnet run
```

### Step 2: Test Sign-In

1. Open your browser and go to: `https://localhost:5001`
2. Click the **"Log in"** or **"Sign in"** button
3. You should now see multiple sign-in options:
   - **Microsoft Account** (personal accounts)
   - **Google** (Google accounts)
   - **Sign up** (if Email signup is enabled)
   - **Sign in with email** (if Email OTP is enabled)

### Step 3: Test Each Provider

**Test Microsoft Account:**
1. Click **"Microsoft Account"** or **"Sign in with Microsoft"**
2. Enter a personal Microsoft account (e.g., @outlook.com)
3. Complete the sign-in process
4. You should be redirected back to the app and logged in

**Test Google:**
1. Sign out if already signed in
2. Click **"Log in"** again
3. Click **"Google"** or **"Sign in with Google"**
4. Enter a Google account (e.g., @gmail.com)
5. Complete the sign-in process
6. You should be redirected back to the app and logged in

**Test Email OTP:**
1. Sign out if already signed in
2. Click **"Log in"** again
3. Click **"Sign in with email"** or **"Email one-time passcode"**
4. Enter any email address
5. Check your email for the verification code
6. Enter the code
7. You should be redirected back to the app and logged in

---

## Troubleshooting

### Error: "You can't sign in here with a personal account"

**Cause**: Microsoft Account provider is not enabled or not added to the user flow

**Solution**:
1. Go to Part 2, Step 2 to add Microsoft Account provider
2. Go to Part 3, Step 2 to add it to your user flow

### Error: "Redirect URI mismatch" (Google)

**Cause**: The redirect URI in Google Cloud Console doesn't match what Azure is using

**Solution**:
1. Go back to Google Cloud Console → Credentials
2. Edit your OAuth client
3. Ensure the redirect URI is exactly:
   ```
   https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp
   ```
4. Save and wait a few minutes for changes to propagate

### Error: "Access token not available"

**Cause**: Token acquisition failed or user hasn't consented to scopes

**Solution**:
- The application code already handles this (we added AccessTokenNotAvailableException handling)
- User will be redirected to sign in again
- Ensure API permissions are granted in app registration

### Google Sign-In Shows Warning Screen

**Cause**: OAuth consent screen is in testing mode

**Solution**:
1. This is normal for development
2. Click **"Continue"** or **"Advanced"** → **"Go to [app name]"**
3. For production, publish your OAuth consent screen:
   - Go to Google Cloud Console → OAuth consent screen
   - Click **"Publish App"**

### Email OTP Not Showing

**Cause**: Provider not enabled or not supported in your tenant

**Solution**:
- Email OTP may not be available in all Entra External ID configurations
- Try enabling "Email signup" instead for local accounts
- Contact Azure support if needed

---

## Verification Checklist

After completing all steps, verify:

- [ ] Google OAuth credentials created in Google Cloud Console
- [ ] Microsoft Account provider added in Azure Portal
- [ ] Google provider added in Azure Portal with correct credentials
- [ ] Email OTP provider enabled (if available)
- [ ] User flow updated to include all identity providers
- [ ] Application tested with Microsoft Account sign-in
- [ ] Application tested with Google sign-in
- [ ] Application tested with Email OTP (if enabled)
- [ ] Users can successfully authenticate and access the API
- [ ] Tokens are acquired and API calls work correctly

---

## Next Steps

1. **Test in Production**: Deploy to Azure and test with production URLs
2. **Add More Providers**: Consider adding Facebook, Apple, or other providers
3. **Configure Conditional Access**: Set up MFA and security policies
4. **Monitor Sign-Ins**: Use Azure Portal → Sign-in logs to monitor authentication
5. **Update Privacy Policy**: Inform users about data collection for each provider

---

## Quick Reference

**Tenant**: letmework4u.onmicrosoft.com
**Authority**: https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com
**Client App ID**: 37b37fa2-c0c4-4766-8bc7-1478524d359d
**API App ID**: f0788507-bdab-4aa8-bf96-857dda053319

**Google Redirect URI**:
```
https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp
```

**Azure Portal Links**:
- Identity Providers: https://portal.azure.com/#view/Microsoft_AAD_IAM/CompanyRelationshipsMenuBlade/~/IdentityProviders
- User Flows: https://portal.azure.com/#view/Microsoft_AAD_IAM/TenantOverview.ReactView
- App Registrations: https://portal.azure.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps

**Google Cloud Console**:
- Project: https://console.cloud.google.com
- Credentials: https://console.cloud.google.com/apis/credentials
- OAuth Consent: https://console.cloud.google.com/apis/credentials/consent
