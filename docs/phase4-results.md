# Phase 4 — Transactional email and auth hardening

**Date:** 26 August 2026
**Branch:** `claude/phase-4-email-auth` (main checkout)
**Verified against:** real Cosmos, over HTTP, with `ASPNETCORE_ENVIRONMENT=Development`

**This was the hard gate on deployment.** Finding 01 of the baseline audit — anonymous account takeover from an email address alone — is closed.

---

## Outcome

| | Before | After |
|---|---|---|
| Token in the HTTP response | **yes, in Development** | **never, in any environment** |
| Tokens at rest | plaintext | **SHA-256 hash** |
| Token entropy | `Guid.NewGuid()` — 122 bits | **256 bits from a CSPRNG** |
| Verification token expiry | **never expired** | 24 hours |
| Reset invalidates sessions | **no** | yes |
| Auth lookups on Cosmos | **cross-partition scans** | single-partition reads |
| Email addresses normalised | on register only | everywhere, incl. OAuth |
| Unit tests | 71 | **85** |
| Integration tests | 43 | **62** |

---

## The leak, and why gating it on the environment was not enough

The working tree had already improved on the audited code: instead of always returning the token, it returned it only when `_env.IsDevelopment()`.

That is a security control whose correctness depends on an environment variable. It has to be right on every host, in every slot, after every configuration change, forever — and it only has to be wrong once to hand anyone who knows an email address a working password-reset token. The blast radius is every account.

So the escape hatch is gone. The token is not in the response in any environment.

The reason it existed was real, though: a developer with no mail provider still needs the link. That need is now met somewhere an attacker cannot reach — `NoOpEmailService` extracts the link from the message body and writes it to the server log:

```
warn: [no email configured] Would have sent "Verify your email - Mi Futuro Trabajo"
      to p4-4d4aefde16@example.com.
      Link: https://localhost:5011/verify-email?token=5Mak78lGSz7A...&email=p4-4d4aefde16@example.com
```

The developer gets the link. Whoever sent the HTTP request does not.

Verified with the API running in **Development**, the exact environment where the old code would have leaked:

```
[ok]  NO token in the forgot-password response
      -> {"message":"If the email exists, a password reset link has been sent"}
```

And the attack itself, as an integration test: scrape every token-shaped string out of the `/forgot-password` response, try each one against `/reset-password`, confirm all are rejected and the original password still works.

---

## Tokens at rest

`SecurityTokens` replaces `Guid.NewGuid().ToString("N")` with 32 bytes from a CSPRNG, base64url-encoded, and stores only a SHA-256 hash.

Two choices that look wrong at a glance and are deliberate:

**A GUID is not a credential.** It is designed to be unique, not unguessable — v4 exposes 122 bits and its generator carries no cryptographic guarantee. These tokens are 256 bits from `RandomNumberGenerator`.

**No salt, no work factor — unlike a password.** A password is low-entropy and human-chosen, so BCrypt exists to make guessing expensive. A 256-bit random token has no dictionary to attack. The only thing hashing must buy here is that *reading the database does not yield a usable credential*, and a fast hash does that. A slow one would only add latency to every redemption.

A test asserts the inverse directly: presenting the stored hash as if it were the token is rejected.

Comparison is `CryptographicOperations.FixedTimeEquals`.

---

## Cosmos: hashing forced a query redesign

Hashing tokens breaks any lookup *by* token — and that lookup was already wrong. `Users` is partitioned by `/Email`, so:

| Query | Partition |
|---|---|
| `.Where(u => u.Email == normalized)` | **single partition** |
| `.Where(u => u.Email.ToLower() == x.ToLower())` | **cross-partition scan** |
| `.Where(u => u.PasswordResetToken == token)` | **cross-partition scan** |

Both of the old forms fanned out across every account in the system — including on **login**, the hottest path in the app.

The fix is that every email-borne link carries `email` alongside `token`. The address is not a secret and grants nothing on its own; the token still has to match. Redemption becomes a single-partition read.

**Measured, not assumed** — and the first attempt to measure it was wrong. My verification script counted `Users` queries whose logged partition was null and reported zero. That check could never have failed: the API logs every partition as `'?'` because sensitive-data logging is off, which is redaction, not a null. I removed the vacuous check and re-measured in a separate probe with sensitive logging on, which is the only way to see the real value:

```
login / register / forgot-password  .Where(u => u.Email == normalized)
    single partition -> probe-90130b9f...@example.com

OLD  .Where(u => u.Email.ToLower() == x.ToLower())
    CROSS-PARTITION SCAN

OLD  .Where(u => u.PasswordResetToken == token)   [by token]
    CROSS-PARTITION SCAN
```

---

## A duplicate-account bug found on the way

OAuth stored `oauthUserInfo.Email` exactly as the provider returned it.

Email is the Cosmos partition key, and Phase 2 established that **Cosmos enforces no unique index** — the one on `User.Email` was removed because it was a false guarantee. So `User@Example.com` and `user@example.com` are not a collision. They are two documents, in two partitions, for one person: sign in with Google, get a second empty account, and none of your applications are there.

All email handling now goes through one `NormalizeEmail`, on every read and every write, OAuth included. Two integration tests pin it: registering the same address in different casing returns `409`, and login accepts any casing.

Account *linking* — an OAuth sign-in matching an existing password account — remains Phase 6.

---

## Also closed

- **Verification tokens never expired.** `User` had `PasswordResetTokenExpiry` but no equivalent for verification, so a link mailed once worked forever. Now 24 hours.
- **Registration never sent anything.** A new user had to find a "resend" button. Registration now issues the verification email directly, best-effort.
- **`VerifyEmail` indexed a dictionary** (`request?["verificationToken"]`), so a body without that key threw `KeyNotFoundException` and surfaced as a 500. It takes a typed `VerifyEmailRequest` and returns 400.
- **Reset did not end existing sessions.** Resetting a password is what someone does when they think an account is compromised; leaving the refresh token alive left the intruder logged in. It is cleared.
- **A completed reset now also confirms the address** — clicking a link sent to that mailbox proves exactly what verification asks.
- **Auth 500s echoed `ex.Message`** to the caller. They no longer do.
- **Email bodies HTML-encode user-supplied values.** A first name lands inside markup delivered to an inbox; unencoded, that is stored XSS with an email client as the sink. Asserted with a `<script>` payload as a registration name.
- **Production fails fast without a mail provider.** An app that silently stops sending password-reset mail is one whose users cannot get back in, and nothing surfaces until someone complains.

---

## Enumeration

`/forgot-password` returns a byte-identical body for known and unknown addresses, and sends nothing for unknown ones. `/verify-email` returns one message for every failure — unknown address, wrong token, expired token — so it cannot be used to test which addresses have accounts.

**Not fixed, and worth stating:** a *timing* oracle remains. For a known address the endpoint writes to the database and calls the mail provider; for an unknown one it returns immediately. The difference is measurable. Closing it properly means moving the send off the request path — which is Phase 5's queue-and-timer work. Noted rather than half-solved.

---

## Verification

**147 tests pass** (85 unit + 62 integration), up from 114.

The integration suite gained a `CapturingEmailService`. Tests now recover a token the way a user does — by reading the email — rather than from the database. That distinction is the point of this phase: a test that could still pull a working token out of the database would be evidence the hashing did nothing. The Phase 1 regression test was rewritten onto it and additionally asserts the stored value is *not* the issued token.

Then 19 checks against real Cosmos over HTTP, in Development: registration mails a working link, verification succeeds and is single-use, reset succeeds, the old password stops working, the token cannot be replayed, casing is irrelevant to login, and no token appears in any response.

---

## Carried forward

- **Rate limiting is Phase 7.** Nothing currently limits how fast `/forgot-password` or `/login` can be called. Until that lands, the enumeration defences above are correct but not cheap to abuse against.
- **ACS is not provisioned.** `NoOpEmailService` is doing the work locally. Phase 7 creates the Communication Services resource, adds the DKIM/SPF/DMARC records for your domain, and sets the two settings — at which point Production will start (and refuse to, if they are missing).
- **Phase 5 inherits the templates.** `EmailTemplates.TaskReminder` and `InterviewReminder` are written and unused; the Functions port will call them.
