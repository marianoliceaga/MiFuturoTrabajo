#!/bin/bash

# Setup Identity Providers for Microsoft Entra External ID
# This script configures multiple identity providers for Letmework4U

# Configuration
TENANT_NAME="letmework4u.onmicrosoft.com"
CLIENT_APP_ID="37b37fa2-c0c4-4766-8bc7-1478524d359d"
API_APP_ID="f0788507-bdab-4aa8-bf96-857dda053319"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

echo -e "${CYAN}========================================"
echo -e "Identity Provider Configuration Script"
echo -e "========================================${NC}"
echo ""

# Step 1: Login to the Entra External ID tenant
echo -e "${YELLOW}Step 1: Logging into Microsoft Entra External ID tenant...${NC}"
echo -e "${GRAY}Tenant: $TENANT_NAME${NC}"
echo ""

az login --tenant "$TENANT_NAME" --allow-no-subscriptions

if [ $? -ne 0 ]; then
    echo -e "${RED}Failed to login. Please check your credentials.${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Successfully logged in${NC}"
echo ""

# Step 2: Check current tenant context
echo -e "${YELLOW}Step 2: Verifying tenant context...${NC}"
TENANT_INFO=$(az account show --query "{tenantId:tenantId, name:name}" -o json)
echo -e "${GRAY}Current Tenant: $(echo $TENANT_INFO | jq -r '.name')${NC}"
echo -e "${GRAY}Tenant ID: $(echo $TENANT_INFO | jq -r '.tenantId')${NC}"
echo ""

# Step 3: Check app registrations
echo -e "${YELLOW}Step 3: Checking app registrations...${NC}"

CLIENT_APP=$(az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?\$filter=appId eq '$CLIENT_APP_ID'" --query "value[0]" -o json 2>/dev/null)

if [ ! -z "$CLIENT_APP" ] && [ "$CLIENT_APP" != "null" ]; then
    APP_NAME=$(echo $CLIENT_APP | jq -r '.displayName')
    SIGN_IN_AUDIENCE=$(echo $CLIENT_APP | jq -r '.signInAudience')
    echo -e "${GREEN}✓ Found Client App: $APP_NAME${NC}"
    echo -e "${GRAY}  App ID: $CLIENT_APP_ID${NC}"
    echo -e "${GRAY}  Sign-in Audience: $SIGN_IN_AUDIENCE${NC}"
else
    echo -e "${YELLOW}⚠ Client app not found in this tenant${NC}"
fi

API_APP=$(az rest --method GET --url "https://graph.microsoft.com/v1.0/applications?\$filter=appId eq '$API_APP_ID'" --query "value[0]" -o json 2>/dev/null)

if [ ! -z "$API_APP" ] && [ "$API_APP" != "null" ]; then
    API_NAME=$(echo $API_APP | jq -r '.displayName')
    echo -e "${GREEN}✓ Found API App: $API_NAME${NC}"
    echo -e "${GRAY}  App ID: $API_APP_ID${NC}"
else
    echo -e "${YELLOW}⚠ API app not found in this tenant${NC}"
fi

echo ""

# Step 4: List current identity providers
echo -e "${YELLOW}Step 4: Checking current identity providers...${NC}"

IDENTITY_PROVIDERS=$(az rest --method GET --url "https://graph.microsoft.com/beta/identity/identityProviders" -o json 2>/dev/null)

if [ ! -z "$IDENTITY_PROVIDERS" ]; then
    PROVIDER_COUNT=$(echo $IDENTITY_PROVIDERS | jq '.value | length')

    if [ "$PROVIDER_COUNT" -gt 0 ]; then
        echo -e "${GREEN}Current Identity Providers:${NC}"
        echo $IDENTITY_PROVIDERS | jq -r '.value[] | "  - \(.displayName) (\(.["@odata.type"]))"'
    else
        echo -e "${YELLOW}No identity providers configured yet.${NC}"
    fi
else
    echo -e "${YELLOW}⚠ Could not list identity providers.${NC}"
fi

echo ""

# Step 5: Configure Microsoft Account Provider
echo -e "${YELLOW}Step 5: Configuring Microsoft Account Identity Provider...${NC}"
echo ""
echo -e "${CYAN}For Microsoft Account provider, configure in the Azure Portal:${NC}"
echo -e "${GRAY}1. Go to: https://portal.azure.com${NC}"
echo -e "${GRAY}2. Navigate to: Microsoft Entra External ID → Identity providers${NC}"
echo -e "${GRAY}3. Click: + Microsoft Account${NC}"
echo -e "${GRAY}4. Follow the wizard to enable${NC}"
echo ""
echo -e "${GRAY}Note: Microsoft Account provider cannot be configured via Graph API for External ID.${NC}"
echo ""

# Step 6: Configure Google Identity Provider
echo -e "${YELLOW}Step 6: Configuring Google Identity Provider...${NC}"
echo ""
echo -e "${CYAN}Before adding Google provider, create OAuth credentials:${NC}"
echo ""
echo -e "${GRAY}A. Google Cloud Console Setup:${NC}"
echo -e "${GRAY}   1. Go to: https://console.cloud.google.com${NC}"
echo -e "${GRAY}   2. Create a new project (or select existing)${NC}"
echo -e "${GRAY}   3. Enable Google+ API (or Google Identity Services)${NC}"
echo -e "${GRAY}   4. Go to: APIs & Services → OAuth consent screen${NC}"
echo -e "${GRAY}      - User Type: External${NC}"
echo -e "${GRAY}      - App name: Letmework4U / MiMejorMomento${NC}"
echo -e "${GRAY}      - Fill in required fields and save${NC}"
echo -e "${GRAY}   5. Go to: APIs & Services → Credentials${NC}"
echo -e "${GRAY}   6. Click: + Create Credentials → OAuth client ID${NC}"
echo -e "${GRAY}   7. Application type: Web application${NC}"
echo -e "${GRAY}   8. Add Authorized redirect URI:${NC}"
echo -e "${CYAN}      https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp${NC}"
echo -e "${GRAY}   9. Click Create and SAVE the Client ID and Client Secret${NC}"
echo ""
echo -e "${GRAY}B. Enter Google OAuth Credentials (or press Enter to skip):${NC}"
read -p "   Google Client ID: " GOOGLE_CLIENT_ID
read -sp "   Google Client Secret: " GOOGLE_CLIENT_SECRET
echo ""

if [ ! -z "$GOOGLE_CLIENT_ID" ] && [ ! -z "$GOOGLE_CLIENT_SECRET" ]; then
    echo ""
    echo -e "${GRAY}   Attempting to add Google identity provider...${NC}"

    GOOGLE_PROVIDER_BODY=$(cat <<EOF
{
    "@odata.type": "microsoft.graph.socialIdentityProvider",
    "displayName": "Google",
    "identityProviderType": "Google",
    "clientId": "$GOOGLE_CLIENT_ID",
    "clientSecret": "$GOOGLE_CLIENT_SECRET"
}
EOF
)

    RESULT=$(az rest --method POST \
        --url "https://graph.microsoft.com/beta/identity/identityProviders" \
        --headers "Content-Type=application/json" \
        --body "$GOOGLE_PROVIDER_BODY" 2>&1)

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}   ✓ Google identity provider added successfully!${NC}"
    else
        echo -e "${YELLOW}   ⚠ Could not add Google provider via API. Please add manually:${NC}"
        echo -e "${GRAY}   1. Go to: Microsoft Entra External ID → Identity providers${NC}"
        echo -e "${GRAY}   2. Click: + Google${NC}"
        echo -e "${GRAY}   3. Enter Client ID: $GOOGLE_CLIENT_ID${NC}"
        echo -e "${GRAY}   4. Enter Client Secret: [your secret]${NC}"
        echo -e "${GRAY}   5. Click Save${NC}"
    fi
else
    echo -e "${YELLOW}   Skipped - You can add this later in the Azure Portal${NC}"
fi

echo ""

# Step 7: Configure Email One-Time Passcode
echo -e "${YELLOW}Step 7: Configuring Email One-Time Passcode (OTP)...${NC}"
echo ""
echo -e "${CYAN}Email OTP configuration for External ID:${NC}"
echo -e "${GRAY}1. Go to: https://portal.azure.com${NC}"
echo -e "${GRAY}2. Navigate to: Microsoft Entra External ID → All identity providers${NC}"
echo -e "${GRAY}3. Look for 'Email one-time passcode' provider${NC}"
echo -e "${GRAY}4. If available, enable it${NC}"
echo ""
echo -e "${GRAY}Note: Email OTP is often enabled by default in External ID.${NC}"
echo ""

# Step 8: Check User Flows
echo -e "${YELLOW}Step 8: Checking User Flows...${NC}"
echo ""

USER_FLOWS=$(az rest --method GET --url "https://graph.microsoft.com/beta/identity/b2cUserFlows" -o json 2>/dev/null)

if [ ! -z "$USER_FLOWS" ]; then
    FLOW_COUNT=$(echo $USER_FLOWS | jq '.value | length')

    if [ "$FLOW_COUNT" -gt 0 ]; then
        echo -e "${GREEN}Found User Flows:${NC}"
        echo $USER_FLOWS | jq -r '.value[] | "  - \(.id)"'
        echo ""
        echo -e "${CYAN}To configure identity providers for user flows:${NC}"
        echo -e "${GRAY}1. Go to: Microsoft Entra External ID → User flows${NC}"
        echo -e "${GRAY}2. Select your sign-up/sign-in flow${NC}"
        echo -e "${GRAY}3. Click: Identity providers${NC}"
        echo -e "${GRAY}4. Enable:${NC}"
        echo -e "${GRAY}   ✓ Microsoft Account${NC}"
        echo -e "${GRAY}   ✓ Google${NC}"
        echo -e "${GRAY}   ✓ Email signup (optional)${NC}"
        echo -e "${GRAY}   ✓ Email one-time passcode (optional)${NC}"
        echo -e "${GRAY}5. Click Save${NC}"
    else
        echo -e "${YELLOW}No user flows found. Create one in the Azure Portal.${NC}"
    fi
else
    echo -e "${YELLOW}⚠ Could not retrieve user flows.${NC}"
fi

echo ""

# Step 9: Summary
echo -e "${CYAN}========================================"
echo -e "Configuration Summary"
echo -e "========================================${NC}"
echo ""
echo -e "${YELLOW}Identity Providers to Configure in Azure Portal:${NC}"
echo ""
echo -e "${GRAY}1. Microsoft Account (for personal accounts)${NC}"
echo -e "${GRAY}   Portal: Microsoft Entra External ID → Identity providers → + Microsoft Account${NC}"
echo ""
echo -e "${GRAY}2. Google (with OAuth credentials)${NC}"
echo -e "${GRAY}   Portal: Microsoft Entra External ID → Identity providers → + Google${NC}"
echo -e "${CYAN}   Redirect URI: https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com/oauth2/authresp${NC}"
echo ""
echo -e "${GRAY}3. Email One-Time Passcode${NC}"
echo -e "${GRAY}   Portal: Microsoft Entra External ID → All identity providers → Email OTP${NC}"
echo ""
echo -e "${GRAY}4. Update User Flow${NC}"
echo -e "${GRAY}   Portal: Microsoft Entra External ID → User flows → Your flow → Identity providers${NC}"
echo ""
echo -e "${YELLOW}Direct Link to Azure Portal:${NC}"
echo -e "${CYAN}https://portal.azure.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/Overview${NC}"
echo ""
echo -e "${CYAN}========================================"
echo -e "${GREEN}Script Complete!${NC}"
echo -e "${CYAN}========================================${NC}"
