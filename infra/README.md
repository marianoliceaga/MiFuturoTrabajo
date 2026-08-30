# Infrastructure

Everything MiFuturoTrabajo runs on, as one Bicep deployment.

## What it costs

| Component | Service | Tier | Cost |
| --- | --- | --- | --- |
| Database | Cosmos DB | Free tier — 1000 RU/s, 25 GB | **$0** |
| API | App Service Linux | F1 Free | **$0** |
| Reminders | Functions | Consumption (1M exec / 400k GB-s free) | **$0** |
| Client | Static Web Apps | Free — custom domain + TLS included | **$0** |
| CV files | Blob Storage | Standard LRS Hot | cents/month |
| Email | Communication Services | Pay-as-you-go, ~$0.25 / 1000 | ~$0 |
| Telemetry | Log Analytics + App Insights | 5 GB/month free | **$0** |

A **$5/month budget alert** is part of the template. Everything above is free-tier or consumption, so any real spend means something changed — a tier, or an exhausted grant. It should never fire.

### Two limits worth knowing before you show this to anyone

**F1 has no Always On.** The site unloads after roughly 20 minutes idle, so the first request after a quiet spell takes several seconds. It also has a 60 CPU-minute daily quota, after which the app returns `403` until midnight UTC.

`appServiceSku = 'B1'` removes both, for about **USD 13/month**. Start on F1; change one value if a cold start ever costs you an impression.

**Cosmos free tier is one account per subscription.** If yours is already spoken for, set `cosmosFreeTier = false` — the account then uses provisioned throughput and is no longer free (~$24/month at 400 RU/s). Check with:

```bash
az cosmosdb list --query "[?enableFreeTier].name" -o tsv
```

## Deploying

```bash
az group create -n rg-mifuturotrabajo -l westeurope
```

```bash
az deployment group create -g rg-mifuturotrabajo -f infra/main.bicep -p infra/main.bicepparam
```

The template is idempotent — re-running it converges rather than duplicating.

## Secrets, which are deliberately not in the template

Anything in a Bicep parameter ends up in deployment history, readable by anyone with access to the resource group. These three are set separately, once.

**JWT signing key.** Generate 48 random bytes and set it:

```bash
az webapp config appsettings set -g rg-mifuturotrabajo -n <api-app-name> --settings "Jwt__SecretKey=$(openssl rand -base64 48)"
```

**Demo account.** Seeding is off unless `Demo__Enabled` is true, and does nothing without a password. It runs once at startup and is idempotent.

```bash
az webapp config appsettings set -g rg-mifuturotrabajo -n <api-app-name> --settings "Demo__Enabled=true" "Demo__Email=demo@yourdomain.example" "Demo__Password=<pick something>"
```

The demo password is *meant* to be public — put it on the login page. Treat that account as compromised by design: it holds nothing real, and per-user quotas cap what anyone can do with it.

**OAuth client IDs.** Public values, but they belong with the rest of the configuration:

```bash
az webapp config appsettings set -g rg-mifuturotrabajo -n <api-app-name> --settings "OAuth__Google__ClientId=<id>" "OAuth__Microsoft__ClientId=<id>" "OAuth__Microsoft__TenantId=common"
```

## After the first deployment

**1. Grant the Functions app access to Storage.** The template assigns *Storage Blob Data Contributor* to the API's identity; the Functions app only needs it if it ever reads CV documents, which today it does not.

**2. Register the redirect URIs.** The hostnames do not exist until the deployment finishes.

```bash
az ad app update --id <client-id> --set spa.redirectUris="['http://localhost:5011/login-callback','https://<static-web-app-hostname>/login-callback']"
```

They must be **SPA**, not **Web**: MSAL.js v2 redeems tokens cross-origin with PKCE, and a Web redirect URI fails with `AADSTS9002326`. Add the same two origins to the Google console as authorised JavaScript origins.

**3. Verify the email domain.** With an Azure-managed domain, mail sends immediately but often lands in spam. With your own domain, ACS generates DKIM and SPF records to add at your registrar, and mail lands in the inbox. Set `emailDomain` in the parameter file to use it.

**4. Point CI at the deployment.** The workflow needs these repository variables and secrets:

| Name | Kind | Value |
| --- | --- | --- |
| `API_APP_NAME` | variable | `apiAppName` output |
| `FUNCTIONS_APP_NAME` | variable | `functionsName` output |
| `API_BASE_URL` | variable | `apiUrl` output + `/api/` |
| `CLIENT_BASE_URL` | variable | `clientUrl` output (no trailing slash) |
| `GOOGLE_CLIENT_ID` | variable | from the Google console |
| `MICROSOFT_CLIENT_ID` | variable | from the Entra registration |
| `AZURE_CLIENT_ID` | secret | federated identity for OIDC login |
| `AZURE_TENANT_ID` | secret | |
| `AZURE_SUBSCRIPTION_ID` | secret | |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | secret | Static Web App deployment token |

Deployment authenticates with **OIDC federated credentials** rather than a stored publish profile, so there is no long-lived credential in the repository to leak.

## Tearing it down

```bash
az group delete -n rg-mifuturotrabajo --yes
```

The budget is subscription-scoped and survives; remove it with `az consumption budget delete`.
