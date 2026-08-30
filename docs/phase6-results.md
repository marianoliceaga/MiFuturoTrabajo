# Phase 6 — OAuth

**Date:** 26 August 2026
**Branch:** `claude/phase-6-oauth` (main checkout)

**Phase 6 asked me to *verify* OAuth end to end. Verifying it first meant finding that Microsoft sign-in accepted forged tokens.** That is a full authentication bypass — worse than finding 01, which at least required knowing an email address and produced a token that had to be redeemed.

---

## Outcome

| | Before | After |
|---|---|---|
| Microsoft token signature | **never checked** | verified against Entra's JWKS |
| Issuer check | substring match, null passed | scheme + host + tenant-id parse |
| Unverified address can claim an existing account | **yes** | no |
| Google sign-in in a browser | **`ReferenceError`** | config-driven |
| Client IDs | literals in `index.html` | `appsettings.json` |
| OAuth sign-up creates an applicant record | **no** | yes |
| Unit tests | 104 | 104 |
| Integration tests | 62 | **88** |

---

## The bypass

`ValidateMicrosoftTokenAsync` did this:

```csharp
var handler = new JwtSecurityTokenHandler();
var jwtToken = handler.ReadJwtToken(idToken);   // decodes. verifies nothing.
```

and then inspected `aud`, `iss` and `exp` **from the decoded claims** — that is, from data the caller wrote. `/api/auth/oauth-login` is `[AllowAnonymous]` and issues a session for whatever address the token names.

So the recipe was: generate an RSA key, mint a JWT with `aud` = the app's client id, `iss` = anything containing `login.microsoftonline.com`, `exp` in the future, `email` = the victim's, and post it. No password. No mailbox access. No contact with Microsoft.

I wrote the attack as a test before changing anything:

```
Failed  ForgedToken_CannotTakeOverAnExistingPasswordAccount
  Assert.Equal() Failure: Values differ
  Expected: Unauthorized
  Actual:   OK
```

`OK`, with an access token and a refresh token for an account that had a password.

Six of the thirteen tests in that file failed against the original code. All thirteen pass now.

### The fix

Real validation: OIDC discovery → Entra's published JWKS → `ValidateToken` with `RequireSignedTokens`, audience, lifetime, and a custom issuer validator. The `ConfigurationManager` is a singleton, so keys are fetched once and refreshed on their own schedule rather than per sign-in, with a 10-second HTTP timeout. If metadata cannot be fetched the sign-in is **refused** — an authentication check must fail closed.

### Why the issuer needed a custom validator

Entra's discovery document returns a *template*:

```
issuer: https://login.microsoftonline.com/{tenantid}/v2.0
```

so the stock comparison against `metadata.Issuer` cannot work for a multi-tenant app. `ValidateMicrosoftIssuer` parses the token's issuer instead and requires https, a Microsoft host, and a **GUID tenant id** in the path. Two tests pin the failures of the old check: a token with no issuer (`!issuer?.Contains(x) == true` is `false` when issuer is null, so it sailed through) and `https://evil.example.com/?next=https://login.microsoftonline.com/` (a substring match accepted it).

---

## nOAuth: a valid signature is not enough

Your registration is `AzureADandPersonalMicrosoftAccount` — **any** tenant can sign in. Entra does not promise the `email` claim is verified: anyone can create a tenant, set a user's mail attribute to someone else's address, and obtain a genuinely Microsoft-signed token naming that address.

If that linked onto an existing account, it would be a takeover with a perfectly valid signature. So:

- `xms_edov` (email domain owner verified) is honoured when present.
- Otherwise, for a multi-tenant app, the address is treated as **unverified**.
- An unverified address may create a *new* account — nothing to steal — but is **refused** when an account with that address already exists, with a message telling the person to sign in with their password.

---

## Account linking

| Case | Behaviour |
|---|---|
| New user, either provider | Account created, applicant record created |
| Returning social user | Same account, no duplicate |
| Different casing | Same account (email is the partition key; Cosmos has no unique index) |
| Existing password user, **verified** address | Linked; password still works; address confirmed; **notification email sent** |
| Existing password user, **unverified** address | Refused; account untouched; password still works |
| New user, unverified address | Allowed, `EmailConfirmed = false` |
| Switching provider, verified | Re-links to the new provider |
| Deactivated account | Refused |

Thirteen tests cover that table, driven through a stubbed validator — the question they ask is what happens *after* a genuinely signed token, which is unreachable through the real validator without Google's or Entra's private keys. Forgery is covered separately by the tests that attack the real one.

**A bug found while writing them:** OAuth sign-up created a `User` but no `Applicant`. Registration creates both. So the first page load after a social sign-up had nothing behind it.

---

## Google sign-in could never have worked

```js
client_id: GOOGLE_CLIENT_ID,   // referenced twice
```

`GOOGLE_CLIENT_ID` is **never assigned anywhere in the file**. Initialisation threw `ReferenceError: GOOGLE_CLIENT_ID is not defined`, so the Google button was inert.

Both providers' identifiers now come from `wwwroot/appsettings.json`, fetched before initialisation, so localhost and the deployed origin differ by configuration rather than by editing markup. Client IDs are public by design — what protects the registration is the redirect-URI allowlist plus the signature check on the API side.

The MSAL block had a related latent break: making the instance async turned the old top-level `const msalInstance` out of scope for `loginWithMicrosoft`. It now awaits an initialisation promise.

Server-side validation was always sound for Google — its library verifies signatures against Google's keys. There is now a test asserting that, so a future refactor cannot quietly turn it into a decode.

---

## What I could not finish, and why

Plan steps 1 and 5 need things I do not have.

**The Entra registration needs changes I did not make.** Modifying your app registration is a change to your account configuration, so I am not doing it unilaterally. What I found:

```
displayName:    Letmework4U Client
signInAudience: AzureADandPersonalMicrosoftAccount
spa:  https://localhost:5001/authentication/login-callback
web:  http://localhost:5011/login-callback
      https://ambitious-sand-077de7b03.3.azurestaticapps.net/login-callback
```

Three problems:

1. **The redirect URIs are on the wrong platform.** `msal.PublicClientApplication` is MSAL.js v2, which redeems tokens cross-origin with PKCE. That requires the **SPA** platform. A `web` redirect URI fails with `AADSTS9002326: Cross-origin token redemption is permitted only for the 'Single-Page Application' client-type`. So Microsoft sign-in is broken for a second, independent reason.
2. **The one SPA entry has the wrong path** — `/authentication/login-callback` (the Blazor MSAL template's path). The app uses `window.location.origin + '/login-callback'`.
3. **The production URI points at a Static Web App that does not exist** in this subscription. Phase 7 creates the real one, and its hostname is not knowable until then.

The fix is to register, as **SPA** redirect URIs, `http://localhost:5011/login-callback` plus the Phase 7 hostname.

> **Correction, added later.** That local URI is wrong: port 5011 is served over **HTTPS**, and 5001 is the HTTP endpoint. The registration now holds `https://localhost:5011/login-callback`, `http://localhost:5001/login-callback` and the deployed origin. `launchSettings.json` was the authority all along; I did not read it. I can run the `az ad app update` for it whenever you say so — it is one command, and it makes most sense once Phase 7 has produced the domain.

**Google's console I cannot reach at all.** A client ID is not queryable without console access, so I cannot tell you whether `762382995830-…` is still valid or which origins it authorises. Its authorised JavaScript origins need the same two entries.

**The six-case matrix against a deployed site is Phase 7 work** — there is no deployed site. Every case in it is covered by an integration test in the meantime, which is not the same thing and is not claimed to be.

---

## Carried forward

- **Entra app registration**: SPA platform, correct paths, Phase 7 hostname. One command, on your say-so.
- **Google console**: authorised JavaScript origins and redirect URIs for localhost and the Phase 7 hostname.
- **The live matrix**: run it after Phase 7 deploys.
- **`ApiBaseUrl` in the client's tracked `appsettings.json`** still points at `letmework4uapi.azurewebsites.net`, which is not in this subscription either. Phase 7 owns it.
- Phase 4's **timing oracle** and Phase 5's **queue-based send** are both still open.
