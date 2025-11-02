# Fix Sign-in Audience for Letmework4U Client App
# This script changes the sign-in audience to allow personal Microsoft accounts and external identity providers

$ClientAppId = "37b37fa2-c0c4-4766-8bc7-1478524d359d"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Fix Sign-in Audience" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Current Issue:" -ForegroundColor Yellow
Write-Host "  Sign-in Audience is set to: AzureADMyOrg" -ForegroundColor Red
Write-Host "  This ONLY allows organizational accounts from your tenant" -ForegroundColor Red
Write-Host ""
Write-Host "Solution:" -ForegroundColor Yellow
Write-Host "  Change to: AzureADandPersonalMicrosoftAccount" -ForegroundColor Green
Write-Host "  This allows:" -ForegroundColor Green
Write-Host "    ✓ Personal Microsoft accounts (@outlook.com, @hotmail.com, etc.)" -ForegroundColor Gray
Write-Host "    ✓ Work/school accounts from any organization" -ForegroundColor Gray
Write-Host "    ✓ External identity providers (Google, etc.) via Entra External ID" -ForegroundColor Gray
Write-Host ""

# Step 1: Get the application object ID
Write-Host "Step 1: Getting application object ID..." -ForegroundColor Yellow

try {
    $app = az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '$ClientAppId'" --query "value[0]" | ConvertFrom-Json

    $objectId = $app.id
    $displayName = $app.displayName
    $currentAudience = $app.signInAudience

    Write-Host "  Found app: $displayName" -ForegroundColor Green
    Write-Host "  Object ID: $objectId" -ForegroundColor Gray
    Write-Host "  Current Audience: $currentAudience" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "  Error: Could not retrieve application. $_" -ForegroundColor Red
    exit 1
}

# Step 2: Update the sign-in audience
Write-Host "Step 2: Updating sign-in audience..." -ForegroundColor Yellow

$updateBody = @{
    signInAudience = "AzureADandPersonalMicrosoftAccount"
} | ConvertTo-Json

try {
    az rest --method PATCH `
        --url "https://graph.microsoft.com/v1.0/applications/$objectId" `
        --headers "Content-Type=application/json" `
        --body $updateBody

    Write-Host "  ✓ Successfully updated sign-in audience!" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "  ⚠ Could not update via API. Error: $_" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Please update manually in Azure Portal:" -ForegroundColor Cyan
    Write-Host "  1. Go to: https://portal.azure.com" -ForegroundColor White
    Write-Host "  2. Navigate to: Microsoft Entra ID → App registrations" -ForegroundColor White
    Write-Host "  3. Find: $displayName" -ForegroundColor White
    Write-Host "  4. Click: Authentication" -ForegroundColor White
    Write-Host "  5. Under 'Supported account types', select:" -ForegroundColor White
    Write-Host "     'Accounts in any organizational directory and personal Microsoft accounts'" -ForegroundColor Green
    Write-Host "  6. Click Save" -ForegroundColor White
    Write-Host ""
    exit 1
}

# Step 3: Verify the change
Write-Host "Step 3: Verifying the change..." -ForegroundColor Yellow

try {
    $verifyApp = az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '$ClientAppId'" --query "value[0]" | ConvertFrom-Json

    $newAudience = $verifyApp.signInAudience

    if ($newAudience -eq "AzureADandPersonalMicrosoftAccount") {
        Write-Host "  ✓ Verified! New audience: $newAudience" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Audience is: $newAudience (expected: AzureADandPersonalMicrosoftAccount)" -ForegroundColor Yellow
    }
    Write-Host ""
} catch {
    Write-Host "  Could not verify. Please check manually in Azure Portal." -ForegroundColor Yellow
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Next Steps" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. ✅ Sign-in audience updated" -ForegroundColor Green
Write-Host ""
Write-Host "2. Now add identity providers:" -ForegroundColor Yellow
Write-Host "   Go to Azure Portal → Microsoft Entra External ID → Identity providers" -ForegroundColor White
Write-Host ""
Write-Host "   Add these providers:" -ForegroundColor White
Write-Host "   • Microsoft Account (for personal accounts)" -ForegroundColor Gray
Write-Host "   • Google (with OAuth credentials)" -ForegroundColor Gray
Write-Host "   • Email One-Time Passcode" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Update your user flow:" -ForegroundColor Yellow
Write-Host "   Microsoft Entra External ID → User flows → [Your flow] → Identity providers" -ForegroundColor White
Write-Host "   Enable all the providers you added" -ForegroundColor White
Write-Host ""
Write-Host "4. Test your application:" -ForegroundColor Yellow
Write-Host "   cd Letmework4U.Client" -ForegroundColor White
Write-Host "   dotnet run" -ForegroundColor White
Write-Host "   Navigate to: https://localhost:5001" -ForegroundColor White
Write-Host ""
Write-Host "You should now be able to sign in with:" -ForegroundColor Green
Write-Host "  ✓ Personal Microsoft accounts" -ForegroundColor Gray
Write-Host "  ✓ Google accounts (after adding provider)" -ForegroundColor Gray
Write-Host "  ✓ Email OTP (after adding provider)" -ForegroundColor Gray
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Script Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
