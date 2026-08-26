# Phase 1 — Structural cleanup, configuration, and F2

**Date:** 26 August 2026
**Applied to:** main checkout working tree (`D:\Projects\MiFuturoTrabajo`), alongside the existing uncommitted work
**Scope:** the approved Phase 1, plus finding **F2** folded in

---

## Outcome

| | Before | After |
|---|---|---|
| Build warnings | 116 | **0** |
| Build errors | 0 | 0 |
| Unit tests | 51 / 52 | **52 / 52** |
| Integration tests | **0 / 31** | **32 / 32** |
| **Total** | 51 / 83 | **84 / 84** |

Every Phase 1 exit criterion is met: the API no longer references the client, the test suites run *and* pass, and no secret remains in a committed file.

---

## 1. Structure

**Api → Client reference removed.** Only the dead `ProjectReference` remained — the `using` and the self-targeting `HttpClient` had already been cleaned up in your working tree. Verified no API source references any `MiFuturoTrabajo.Client` type.

`MiFuturoTrabajo.Api` now depends on `Infrastructure` and `Shared` only.

---

## 2. F2 — implicit `[Required]` on non-nullable DTO strings

The root cause of "a company with only a name returns 400 on five optional fields".

1. **`Program.cs`** — `AddControllers` now sets
   `options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`.
   DTOs describe the wire shape; they are not a required-field contract.
2. **`DTO.cs`** — 60 non-nullable string properties defaulted to `string.Empty`, so optional fields are never `null` downstream. This alone removed most of the 116 warnings.
3. **`MappingService.cs`** — `Id = dto.Id ?? Guid.NewGuid().ToString()` caught `null` but not `""`, silently persisting entities with an empty key. Replaced with `string.IsNullOrWhiteSpace(...)` in **9** mappers.

Guarded by a new regression test, `Company_CreateWithOnlyName_Succeeds`, which posts **raw JSON with explicit nulls** — serialising a `CompanyDto` would send empty strings and pass whether or not the fix is present.

---

## 3. Configuration and secrets

- **`Jwt:SecretKey` rotated** and moved to `dotnet user-secrets` (`UserSecretsId` added to the API project). The old key is in git history and is now dead.
- **Removed** the `SecretKey` line from committed `appsettings.Development.json` rather than blanking it — an empty string would have *overridden* the value coming from `appsettings.json`. A comment documents the `user-secrets` command.
- **Added committed `appsettings.Testing.json`** with a deterministic, clearly-labelled test-only key. This is what makes the integration suite self-contained: previously it depended on `appsettings.json`, which is gitignored, so the suite could never pass on a clean clone.

---

## 4. Test suite: from aspirational to real

The integration tests had **never passed**. Two independent causes:

### 4a. Wrong routes (46 fixes)
The suite was written against the README's documented API, not the controllers. The README documents plural REST routes; the actual API is singular with RPC-style action names.

| Test called | API actually serves |
|---|---|
| `POST /api/companies` | `POST /api/companies/CompanyAdd` |
| `GET /api/jobapplications/{id}` | `GET /api/jobapplication/JobApplicationGetById/{id}` |
| `PUT /api/meetings/{id}` | `PATCH /api/meeting/{id}` |
| `DELETE /api/recruiters/{id}` | `DELETE /api/recruiters/RecruiterDeleteById/{id}` |

All `PUT` calls became `PATCH`, matching the controllers. Route names are unaffected by Phase 2, so this work is durable.

### 4b. A test that asserted the vulnerability
`ForgotPassword_ResetPassword_FullFlow_Success` asserted that the response **contains** `resetToken` — encoding the account-takeover hole as expected behaviour. Left alone, it would have blocked the security fix.

Rewritten to read the token from the database (as the real email flow does) and to assert `Assert.DoesNotContain("resetToken", ...)` — turning the test into a regression guard against the leak instead of a guarantee of it.

---

## 5. Two real bugs the restored tests then caught

### Soft-delete was inconsistent
`DELETE` sets `IsDeleted = true`. `GetAll` and the board filtered on it; **`GetById` did not**, so a deleted application still returned `200`. Added `&& !ja.IsDeleted` to all **4** single-entity queries (get, update, delete, status update), so operating on a deleted application now correctly `404`s.

### `VerifyPassword` contract vs. OAuth accounts
The unit test expected `ArgumentNullException` for a null hash. But OAuth-only users legitimately have `PasswordHash == null` ([`User.cs:20`](../MiFuturoTrabajo.Infrastructure/Entities/User.cs)), and `AuthController` passes it straight in — so throwing would return a 500 on every password-login attempt against an OAuth account.

The implementation was right; the test was wrong. Signature is now `VerifyPassword(string password, string? passwordHash)` with an explicit null check returning `false`, and the test asserts that contract. This also cleared the two `CS8604` warnings at the call sites.

---

## Files changed

| File | Change |
|---|---|
| `MiFuturoTrabajo.Api.csproj` | dropped Client reference; added `UserSecretsId` |
| `Api/Program.cs` | suppressed implicit `[Required]` |
| `Api/Services/PasswordHasher.cs` | nullable hash contract |
| `Api/Services/MappingService.cs` | 9 × empty-string id fix |
| `Api/Controllers/JobApplicationController.cs` | 4 × soft-delete filter |
| `Api/appsettings.Development.json` | secret removed |
| `Api/appsettings.Testing.json` | **new** |
| `Shared/DTO.cs` | 60 properties defaulted |
| `Tests.Unit/Services/PasswordHasherTests.cs` | rewritten null-hash test |
| `Tests.Integration/Tests/*.cs` | 46 route fixes, leak-assertion rewritten, F2 regression test added |

---

## Carried forward

- **F3 / F4** (duplicate reminder mail; three-level cross-container includes) are deliberately untouched — both are resolved during the Phase 5 port to Functions.
- **F6** (README route documentation) belongs to Phase 8.
- The `localhost:8081` → InMemory sniff still stands; Phase 2 replaces it with an explicit `Persistence:Provider` setting.
- Everything remains **uncommitted** in the main checkout, now on top of the pre-existing 34 changes.
