# Setup Identity Providers for Microsoft Entra External ID
# This script configures multiple identity providers for Letmework4U

# Configuration
$TenantName = "letmework4u.onmicrosoft.com"
$ClientAppId = "37b37fa2-c0c4-4766-8bc7-1478524d359d"
$ApiAppId = "f0788507-bdab-4aa8-bf96-857dda053319"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Identity Provider Configuration Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Login to the Entra External ID tenant
Write-Host "Step 1: Logging into Microsoft Entra External ID tenant..." -ForegroundColor Yellow
Write-Host "Tenant: $TenantName" -ForegroundColor Gray
Write-Host ""

try {
    # Login allowing no subscriptions (Entra External ID may not have subscriptions)
    az login --tenant $TenantName --allow-no-subscriptions

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to login. Please check your credentials." -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ Successfully logged in" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "Error during login: $_" -ForegroundColor Red
    exit 1
}

# Step 2: Check current tenant context
Write-Host "Step 2: Verifying tenant context..." -ForegroundColor Yellow
$tenantInfo = az account show --query "{tenantId:tenantId, name:name}" -o json | ConvertFrom-Json
Write-Host "Current Tenant ID: $($tenantInfo.tenantId)" -ForegroundColor Gray
Write-Host "Current Tenant Name: $($tenantInfo.name)" -ForegroundColor Gray
Write-Host ""

# Step 3: Check app registrations
Write-Host "Step 3: Checking app registrations..." -ForegroundColor Yellow

try {
    # Get client app details
    $clientApp = az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '$ClientAppId'" --query "value[0]" -o json | ConvertFrom-Json

    if ($clientApp) {
        Write-Host "✓ Found Client App: $($clientApp.displayName)" -ForegroundColor Green
        Write-Host "  App ID: $($clientApp.appId)" -ForegroundColor Gray
        Write-Host "  Sign-in Audience: $($clientApp.signInAudience)" -ForegroundColor Gray
    } else {
        Write-Host "⚠ Client app not found in this tenant" -ForegroundColor Yellow
    }

    # Get API app details
    $apiApp = az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '$ApiAppId'" --query "value[0]" -o json | ConvertFrom-Json

    if ($apiApp) {
        Write-Host "✓ Found API App: $($apiApp.displayName)" -ForegroundColor Green
        Write-Host "  App ID: $($apiApp.appId)" -ForegroundColor Gray
    } else {
        Write-Host "⚠ API app not found in this tenant" -ForegroundColor Yellow
    }

    Write-Host ""
} catch {
    Write-Host "⚠ Could not retrieve app registrations. This might be expected for Entra External ID." -ForegroundColor Yellow
    Write-Host ""
}

# Step 4: List current identity providers
Write-Host "Step 4: Checking current identity providers..." -ForegroundColor Yellow

try {
    $identityProviders = az rest --method GET --url "https://graph.microsoft.com/beta/identity/identityProviders" -o json | ConvertFrom-Json

    if ($identityProviders.value.Count -gt 0) {
        Write-Host "Current Identity Providers:" -ForegroundColor Green
        foreach ($provider in $identityProviders.value) {
            Write-Host "  - $($provider.displayName) ($($provider.'@odata.type'))" -ForegroundColor Gray
        }
    } else {
        Write-Host "No identity providers configured yet." -ForegroundColor Yellow
    }
    Write-Host ""
} catch {
    Write-Host "⚠ Could not list identity providers. Graph API permissions may be required." -ForegroundColor Yellow
    Write-Host ""
}

# Step 5: Configure Microsoft Account Provider
Write-Host "Step 5: Configuring Microsoft Account Identity Provider..." -ForegroundColor Yellow
Write-Host ""
Write-Host "For Microsoft Account provider, you need to configure this in the Azure Portal:" -ForegroundColor Cyan
Write-Host "1. Go to: https://portal.azure.com" -ForegroundColor White
Write-Host "2. Navigate to: Microsoft Entra External ID → Identity providers" -ForegroundColor White
Write-Host "3. Click: + Microsoft Account" -ForegroundColor White
Write-Host "4. Follow the wizard to enable" -ForegroundColor White
Write-Host ""
Write-Host "Note: Microsoft Account provider cannot be configured via Graph API for External ID." -ForegroundColor Gray
Write-Host ""

# Step 6: Configure Google Identity Provider
Write-Host "Step 6: Configuring Google Identity Provider..." -ForegroundColor Yellow
Write-Host ""
Write-Host "Before adding Google provider, you need to create OAuth credentials:" -ForegroundColor Cyan
Write-Host ""
Write-Host "A. Google Cloud Console Setup:" -ForegroundColor White
Write-Host "   1. Go to: https://console.cloud.google.com" -ForegroundColor White
Write-Host "   2. Create a new project (or select existing)" -ForegroundColor White
Write-Host "   3. Enable Google+ API (or Google Identity Services)" -ForegroundColor White
Write-Host "   4. Go to: APIs & Services → OAuth consent screen" -ForegroundColor White
Write-Host "      - User Type: External" -ForegroundColor White
Write-Host "      - App name: Letmework4U / MiMejorMomento" -ForegroundColor White
Write-Host "      - Fill in required fields and save" -ForegroundColor White
Write-Host "   5. Go to: APIs & Services → Credentials" -ForegroundColor White
Write-Host "   6. Click: + Create Credentials → OAuth client ID" -ForegroundColor White
Write-Host "   7. Application type: Web application" -ForegroundColor White
Write-Host "   8. Add Authorized redirect URI:" -ForegroundColor White
Write-Host "      https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp" -ForegroundColor Cyan
Write-Host "   9. Click Create and SAVE the Client ID and Client Secret" -ForegroundColor White
Write-Host ""
Write-Host "B. Enter Google OAuth Credentials (or press Enter to skip):" -ForegroundColor White
$googleClientId = Read-Host "   Google Client ID"
$googleClientSecret = Read-Host "   Google Client Secret" -AsSecureString

if ($googleClientId -and $googleClientSecret) {
    # Convert SecureString to plain text
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($googleClientSecret)
    $googleClientSecretPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)

    Write-Host ""
    Write-Host "   Attempting to add Google identity provider..." -ForegroundColor Gray

    $googleProviderBody = @{
        "@odata.type" = "microsoft.graph.socialIdentityProvider"
        displayName = "Google"
        identityProviderType = "Google"
        clientId = $googleClientId
        clientSecret = $googleClientSecretPlain
    } | ConvertTo-Json

    try {
        az rest --method POST `
            --url "https://graph.microsoft.com/beta/identity/identityProviders" `
            --headers "Content-Type=application/json" `
            --body $googleProviderBody

        Write-Host "   ✓ Google identity provider added successfully!" -ForegroundColor Green
    } catch {
        Write-Host "   ⚠ Could not add Google provider via API. Please add manually in Azure Portal:" -ForegroundColor Yellow
        Write-Host "   1. Go to: Microsoft Entra External ID → Identity providers" -ForegroundColor White
        Write-Host "   2. Click: + Google" -ForegroundColor White
        Write-Host "   3. Enter Client ID: $googleClientId" -ForegroundColor White
        Write-Host "   4. Enter Client Secret: [your secret]" -ForegroundColor White
        Write-Host "   5. Click Save" -ForegroundColor White
    }
} else {
    Write-Host "   Skipped - You can add this later in the Azure Portal" -ForegroundColor Yellow
}
Write-Host ""

# Step 7: Configure Email One-Time Passcode
Write-Host "Step 7: Configuring Email One-Time Passcode (OTP)..." -ForegroundColor Yellow
Write-Host ""
Write-Host "Email OTP configuration for External ID:" -ForegroundColor Cyan
Write-Host "1. Go to: https://portal.azure.com" -ForegroundColor White
Write-Host "2. Navigate to: Microsoft Entra External ID → All identity providers" -ForegroundColor White
Write-Host "3. Look for 'Email one-time passcode' provider" -ForegroundColor White
Write-Host "4. If available, enable it" -ForegroundColor White
Write-Host ""
Write-Host "Note: Email OTP is often enabled by default in External ID." -ForegroundColor Gray
Write-Host ""

# Step 8: Check User Flows
Write-Host "Step 8: Checking User Flows..." -ForegroundColor Yellow
Write-Host ""

try {
    $userFlows = az rest --method GET --url "https://graph.microsoft.com/beta/identity/b2cUserFlows" -o json | ConvertFrom-Json

    if ($userFlows.value.Count -gt 0) {
        Write-Host "Found User Flows:" -ForegroundColor Green
        foreach ($flow in $userFlows.value) {
            Write-Host "  - $($flow.id)" -ForegroundColor Gray
        }
        Write-Host ""
        Write-Host "To configure identity providers for user flows:" -ForegroundColor Cyan
        Write-Host "1. Go to: Microsoft Entra External ID → User flows" -ForegroundColor White
        Write-Host "2. Select your sign-up/sign-in flow" -ForegroundColor White
        Write-Host "3. Click: Identity providers" -ForegroundColor White
        Write-Host "4. Enable:" -ForegroundColor White
        Write-Host "   ✓ Microsoft Account" -ForegroundColor White
        Write-Host "   ✓ Google" -ForegroundColor White
        Write-Host "   ✓ Email signup (optional)" -ForegroundColor White
        Write-Host "   ✓ Email one-time passcode (optional)" -ForegroundColor White
        Write-Host "5. Click Save" -ForegroundColor White
    } else {
        Write-Host "No user flows found. You may need to create one in the Azure Portal." -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ Could not retrieve user flows." -ForegroundColor Yellow
    Write-Host "Please configure user flows manually in the Azure Portal." -ForegroundColor White
}
Write-Host ""

# Step 9: Summary and Next Steps
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Identity Providers to Configure in Azure Portal:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Microsoft Account (for personal accounts)" -ForegroundColor White
Write-Host "   Portal: Microsoft Entra External ID → Identity providers → + Microsoft Account" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Google (with OAuth credentials)" -ForegroundColor White
Write-Host "   Portal: Microsoft Entra External ID → Identity providers → + Google" -ForegroundColor Gray
Write-Host "   Redirect URI: https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. Email One-Time Passcode" -ForegroundColor White
Write-Host "   Portal: Microsoft Entra External ID → All identity providers → Email OTP" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Update User Flow" -ForegroundColor White
Write-Host "   Portal: Microsoft Entra External ID → User flows → Your flow → Identity providers" -ForegroundColor Gray
Write-Host "   Enable all providers you want to support" -ForegroundColor Gray
Write-Host ""
Write-Host "Direct Link to Azure Portal:" -ForegroundColor Yellow
Write-Host "https://portal.azure.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/Overview" -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Script Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
