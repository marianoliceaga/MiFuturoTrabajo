# OAuth setup

Google and Microsoft sign-in, hybrid: the provider proves who you are, this API issues its own JWT. Provider tokens are never used as session tokens.

> **If you have read an older version of this file, re-read the redirect URIs below.** The previous version specified `https://localhost:5001/authentication/login-callback` — the wrong port *and* the wrong path. Registering that is why Microsoft sign-in currently fails.

---

## The flow

```
Browser ──> Google / Entra          user signs in
        <── ID token
        ──> POST /api/Auth/oauth-login  { idToken, provider }
                    │
                    ├─ verify the signature against the provider's published keys
                    ├─ check audience, issuer, expiry
                    ├─ find or create the user
        <───────────┴─ this API's own access + refresh token
```

The provider's token is validated and discarded. Everything afterwards uses this application's JWT, exactly as email/password sign-in does.

---

## The redirect URIs, which are the part people get wrong

The Blazor client calls `window.location.origin + '/login-callback'`. So the URIs to register are:

| Environment | URI |
|---|---|
| Local | `http://localhost:5011/login-callback` |
| Deployed | `https://ambitious-coast-084594d03.7.azurestaticapps.net/login-callback` |

Two mistakes to avoid, both of which have already been made in this project:

- **Not `/authentication/login-callback`.** That is the path from the Blazor WASM MSAL project template. This app does not use it.
- **Not port 5001.** The client's HTTPS profile is **5011**; 5001 is its HTTP fallback.

### Microsoft: SPA, not Web

The app uses `msal.PublicClientApplication` — MSAL.js v2 — which redeems the authorization code **cross-origin with PKCE**. That requires the redirect URI to be registered under the **Single-page application** platform.

Registered under **Web**, sign-in fails with:

```
AADSTS9002326: Cross-origin token redemption is permitted only for the
'Single-Page Application' client-type.
```

```bash
az ad app update --id <client-id> --set spa.redirectUris="['http://localhost:5011/login-callback','https://<your-static-web-app>/login-callback']"
```

---

## Google

1. [Google Cloud Console](https://console.cloud.google.com) → new project.
2. **APIs & Services → OAuth consent screen.** External. Add your email as a test user while it is unpublished.
3. **Credentials → Create credentials → OAuth client ID → Web application.**
4. **Authorised JavaScript origins** — the origin only, no path:
   - `http://localhost:5011`
   - `https://<your-static-web-app>.azurestaticapps.net`
5. **Authorised redirect URIs** — the two from the table above.
6. Copy the client ID.

## Microsoft

1. [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID → App registrations → New registration**.
2. Supported account types: *Accounts in any organizational directory and personal Microsoft accounts* — this is what makes it work for any Microsoft account, and it has a security consequence (see below).
3. **Authentication → Add a platform → Single-page application.** Add both URIs.
4. Copy the Application (client) ID.

---

## Where the client IDs go

They are **public values** — half of a pair whose other half never leaves Google or Entra. What protects the registration is the redirect-URI allowlist plus signature validation on the API side, not the secrecy of the ID.

**Client** — `MiFuturoTrabajo.Client/wwwroot/appsettings.json`, fetched at runtime before OAuth initialises, so localhost and the deployed origin differ by configuration rather than by editing markup:

```json
{
  "OAuth": {
    "Google":    { "ClientId": "…apps.googleusercontent.com" },
    "Microsoft": { "ClientId": "…", "Authority": "https://login.microsoftonline.com/common" }
  }
}
```

**API** — the same IDs, because it validates the `aud` claim against them:

```bash
az webapp config appsettings set -g <rg> -n <api> --settings \
  "OAuth__Google__ClientId=<id>" \
  "OAuth__Microsoft__ClientId=<id>" \
  "OAuth__Microsoft__TenantId=common"
```

Locally, put them in `appsettings.Development.json` — they are not secrets.

---

## How tokens are validated

**Google** — `GoogleJsonWebSignature.ValidateAsync` fetches Google's keys and verifies signature, audience, issuer and lifetime.

**Microsoft** — OIDC discovery to Entra's JWKS, then `ValidateToken` with `RequireSignedTokens`, audience, lifetime, and a custom issuer validator. The custom validator is necessary because Entra's metadata returns a *templated* issuer (`https://login.microsoftonline.com/{tenantid}/v2.0`), which the stock comparison cannot match for a multi-tenant app. It parses the token's issuer and requires HTTPS, a Microsoft host, and a GUID tenant id in the path.

If the keys cannot be fetched, the sign-in is **refused**. An authentication check must fail closed.

> An earlier implementation called `JwtSecurityTokenHandler.ReadJwtToken`, which decodes and verifies **nothing**, then inspected `aud`, `iss` and `exp` from those decoded — that is, caller-supplied — claims. Anyone could mint a token naming any address and sign in as them. `OAuthSecurityTests` forges tokens against the current validator so that cannot return.

---

## Account linking

| Case | Result |
|---|---|
| New address | Account created |
| Returning social user | Same account |
| Different casing of the same address | Same account |
| Existing password account, provider **verified** the address | Linked; password still works; notification email sent |
| Existing password account, address **not verified** | **Refused** |

That last row is the important one. With `signInAudience` set to any organizational directory plus personal accounts, **anyone can create a tenant, set a user's mail attribute to someone else's address, and obtain a genuinely Microsoft-signed token naming it** — the "nOAuth" pattern. Linking that onto an existing account would be a takeover with a valid signature.

So Entra's `email` claim is treated as verified only when `xms_edov` says so, or when the app is pinned to a single tenant. Otherwise an unverified address may create a *new* account — nothing to steal — but is refused when one already exists.

---

## Testing locally

1. Register both localhost URIs as above.
2. Run the API and client (see [`docs/local-setup.md`](docs/local-setup.md)).
3. Sign in with each provider.

Cases worth walking, since they are the ones that break:

- A brand-new address via each provider.
- The same provider twice — one account, not two.
- An address that already has a password account — links, password still works.
- The same address in different casing — one account.

## Troubleshooting

**`AADSTS9002326`** — redirect URI is on the Web platform. It must be SPA.

**`redirect_uri_mismatch`** — the registered URI does not match `origin + /login-callback` exactly. Check the port (5011, not 5001) and the path (no `/authentication` prefix).

**"Invalid OAuth token or provider"** — the API refused the token. Its log says why: signature, audience, issuer, expiry, or metadata that could not be fetched.

**Google button does nothing** — `OAuth:Google:ClientId` is empty in the client's `appsettings.json`; the console logs a warning and initialisation stops.

**Sign-in succeeds but a second account appears** — an email-normalisation bug. All email handling goes through one normalizer precisely because email is the Cosmos partition key and Cosmos enforces no unique index.
