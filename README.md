# MiFuturoTrabajo

A job-application tracker: Kanban board, companies, recruiters, interviews, tasks, and CV versions. Blazor WebAssembly front end, ASP.NET Core API, Azure Cosmos DB, and two Azure Functions timer triggers for reminders.

**[Live demo](https://ambitious-coast-084594d03.7.azurestaticapps.net)** · `demo@mifuturotrabajo.app` / `DemoAccount2026!`

The demo account is seeded with a realistic three-month job hunt. Its credentials are meant to be public; per-user quotas cap what anyone can do with it.

> The API runs on App Service **F1 Free**, which has no Always On. If the site has been idle for ~20 minutes the first request takes a few seconds while it wakes up.

---

## What actually works

Everything listed here has been exercised against the deployed environment, not only locally.

| | |
|---|---|
| Job applications | Kanban board with drag-and-drop across 8 statuses, list view, soft delete |
| Companies & recruiters | Full CRUD, scoped per user |
| Interviews | Meetings with per-meeting email reminder lead times |
| Tasks | Deadlines, completion, daily reminder digest |
| CV versions | Real file upload to Blob Storage, expiring download links, default selection |
| Job references | Past employment, embedded in the applicant document |
| Auth | Email/password with BCrypt, refresh tokens, account lockout, Google and Microsoft sign-in |
| Email | Verification and password reset via Azure Communication Services |
| Abuse control | Rate limiting on auth and per-user, plus per-user storage quotas |

### Known limitations

Stated plainly, because a README that hides these is worse than one that admits them.

- **Transactional email does not send.** The ACS domain `ideapixel.com.ar` is provisioned, but its DKIM, SPF and domain-verification records have not been added at Cloudflare, so all five checks read `NotStarted`. Registration and password reset both still succeed and log that the message could not be sent — the send path is deliberately non-fatal — but no mail arrives. This is the one limitation that stops a listed feature from working.
- **Microsoft sign-in is not verified end to end.** The Entra registration was wrong and is now fixed: the redirect URIs sit on the `SPA` platform (MSAL.js v2 redeems cross-origin with PKCE, so `Web` returns `AADSTS9002326`) with the correct `/login-callback` path. Server-side token validation is implemented and covered by 13 tests including forged-token attacks. What has not happened is a human completing an actual Microsoft sign-in against the deployed site.
- **Google sign-in needs its origins registered.** The deployed origin has not been added to the OAuth client's authorised JavaScript origins in the Google Cloud console. Server-side validation was always sound — `GoogleJsonWebSignature.ValidateAsync` verifies against Google's keys — and the client-side bug that made it impossible is fixed, so this is the only thing left for it.
- **Cosmos runs on serverless, not the free tier.** The free-tier slot in this subscription had not released when the deployment ran. Serverless costs cents per month at this volume; switching means creating a new account, since billing mode cannot be changed in place.

---

## Architecture

```
Blazor WebAssembly  ──HTTPS──>  ASP.NET Core API  ──>  Cosmos DB (7 containers)
(Static Web Apps)                (App Service F1)  ──>  Blob Storage (CV files)
                                                   ──>  Communication Services (email)

Azure Functions (Consumption)  ──>  Cosmos DB
  TaskReminders       daily 08:00 UTC
  InterviewReminders  every 15 minutes
```

Projects:

```
MiFuturoTrabajo.Client/             Blazor WebAssembly front end
MiFuturoTrabajo.Api/                ASP.NET Core Web API
MiFuturoTrabajo.Functions/          Reminder timer triggers (isolated worker)
MiFuturoTrabajo.Infrastructure/     EF Core, entities, email, reminder logic
MiFuturoTrabajo.Shared/             DTOs and enums, shared by client and API
MiFuturoTrabajo.Tests.Unit/         104 tests
MiFuturoTrabajo.Tests.Integration/  92 tests
infra/                              Bicep templates
```

`Api` references `Infrastructure` and `Shared` only. `Functions` references the same two — email and reminder logic live in `Infrastructure` precisely so both hosts can share them without a web project in the middle.

For the reasoning behind the significant decisions, see [`docs/architecture.md`](docs/architecture.md) and the ADRs in [`docs/decisions/`](docs/decisions/).

### Cosmos DB schema

Seven containers. Aggregates are embedded rather than joined, because **the EF Core Cosmos provider cannot join across containers** — so every read is a single-partition query.

| Container | Partition key | Embedded |
|---|---|---|
| `Users` | `/Email` | — |
| `Applicants` | `/UserId` | `CVVersions`, `PastJobReferences` |
| `JobApplications` | `/ApplicantId` | `InterviewSteps`, `Company` snapshot, `Notes`, `Labels`, `RecruiterIds` |
| `Meetings` | `/ApplicantId` | `RecruiterIds` |
| `ApplicantTasks` | `/ApplicantId` | — |
| `Companies` | `/UserId` | `Labels` |
| `Recruiters` | `/UserId` | — |

The `Company` snapshot on a job application is a deliberate denormalisation: the board needs a company name on every card and Cosmos cannot join. A company rename fans out across that user's applications, bounded by one single-partition query.

### Job application statuses

`NotApplied` · `Applied` · `FirstInterview` · `Meetings` · `Rejected` · `Offer` · `Completed` · `NoResponse`

---

## Running locally

### Prerequisites

- .NET 8 SDK
- [Azurite](https://github.com/Azure/Azurite) for blob storage — `npx azurite`
- No Cosmos emulator needed: local development uses the EF InMemory provider by default

### Configuration

Secrets go in user-secrets, not in a file:

```bash
dotnet user-secrets --project MiFuturoTrabajo.Api set "Jwt:SecretKey" "$(openssl rand -base64 48)"
```

`appsettings.Development.json` holds the non-secret defaults and is committed. It sets `Persistence:Provider` to `InMemory`; set it to `Cosmos` and supply `CosmosDb:ConnectionString` to run against a real account.

With no `AzureCommunicationServices` configured, email is not sent — instead the verification and reset **links are written to the API log**, which is where to look when testing those flows locally.

### Run

```bash
npx azurite
```

```bash
dotnet run --project MiFuturoTrabajo.Api
```

```bash
dotnet run --project MiFuturoTrabajo.Client
```

| | |
|---|---|
| API | `https://localhost:7071` |
| Swagger | `https://localhost:7071/swagger` |
| Client | `https://localhost:5011` |

### Reminder functions

```bash
cd MiFuturoTrabajo.Functions
cp local.settings.json.example local.settings.json
func start
```

`local.settings.json` is gitignored — it holds connection strings. The Functions app has no InMemory option: pointed at an empty throwaway database it would read nothing and mail nobody, which looks exactly like working correctly.

---

## Tests

```bash
dotnet test
```

**196 tests, all passing.** No emulator, no storage account, no network: the suite ships its own `appsettings.Testing.json`, an in-memory blob store, and a capturing mail service.

| Unit (104) | | Integration (92) | |
|---|---|---|---|
| `ReminderServiceTests` | 19 | `EmailAndTokenSecurityTests` | 19 |
| `CvFileValidatorTests` | 17 | `OAuthSecurityTests` | 13 |
| `JwtServiceTests` | 15 | `OAuthAccountLinkingTests` | 13 |
| `MappingServiceTests` | 15 | `AuthenticationFlowTests` | 12 |
| `AuthControllerTests` | 14 | `CvUploadTests` | 11 |
| `SecurityTokensTests` | 14 | `JobApplicationCrudTests` | 10 |
| `PasswordHasherTests` | 10 | `KanbanBoardTests` | 10 |
| | | `QuotaEnforcementTests` | 2 |
| | | `RateLimiterTests` | 2 |

Several of these exist because they caught something real: `EmailAndTokenSecurityTests` reproduces an account-takeover path that used to work, and `OAuthSecurityTests` forges Microsoft tokens against a validator that used to accept them.

---

## API

Routes follow the controller names, which are a mix of REST and RPC-style actions. This is what the code does, not what a tidier design would do.

### Auth — `/api/Auth`

| | |
|---|---|
| `POST /register` | Create an account; sends a verification email |
| `POST /login` | Email and password |
| `POST /logout` | Revokes the refresh token |
| `POST /refresh` | New access token |
| `GET /me` | Current user |
| `POST /oauth-login` | Google or Microsoft ID token |
| `POST /change-password` | |
| `POST /forgot-password` | Always returns the same neutral message |
| `POST /reset-password` | Email + token + new password |
| `POST /send-verification` | Resend |
| `POST /verify-email` | Email + token |

Rate limited to 10 requests per minute per client address.

### Everything else

All limited to 120 requests per minute per user.

| Area | Base | Notable routes |
|---|---|---|
| Job applications | `/api/JobApplication` | `GET /board`, `PATCH /{id}/status`, `POST /JobApplicationAdd` |
| Companies | `/api/Companies` | `POST /CompanyAdd`, `GET /CompanyGetById/{id}` |
| Recruiters | `/api/Recruiters` | `POST /RecruiterAdd` |
| Meetings | `/api/Meeting` | `POST /MeetingAdd` |
| Tasks | `/api/ApplicantTask` | `POST /ApplicantTaskAdd` |
| CV versions | `/api/CVVersion` | `POST /upload` (multipart), `GET /{id}/download` |
| Applicant | `/api/Applicant` | `GET /current`, `POST /UploadCV`, `GET /CV/{cvId}/download` |
| Health | `/health` | Anonymous |

Updates are `PATCH`, not `PUT`. A CV download returns a **short-lived link**, not the file — the blob container is private and the URL expires in 15 minutes.

### Quotas

Per account: 200 job applications · 100 companies · 100 recruiters · 300 meetings · 300 tasks · 10 CV versions. Exceeding one returns `429` with a message naming the limit.

---

## Deployment

**Pushing to `main` deploys.** `deploy.yml` runs the full build-and-test gate, then publishes the API, the Functions app and the client, and finishes by smoke-testing `/health` — a deploy that reports success while the site returns 500 is worse than a failed one. It authenticates with an OIDC federated credential scoped to the `production` environment, so there is no publish profile or long-lived secret in the repository.

Infrastructure is Bicep, in [`infra/`](infra/). See [`infra/README.md`](infra/README.md) for what it costs, what has to be set by hand, and why.

```bash
az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
```

Everything sits on a free tier or a consumption grant except Blob Storage and Communication Services, which cost cents per month at demo volume. A $5 budget alert is part of the template and should never fire.

The Function App deploys into a **separate resource group**: Azure will not create a Linux Consumption plan in a group that already holds a Linux Free/Basic plan.

---

## OAuth setup

See [`OAUTH_SETUP.md`](OAUTH_SETUP.md). The short version: client IDs are public and live in `MiFuturoTrabajo.Client/wwwroot/appsettings.json`; what protects the registration is the redirect-URI allowlist plus signature validation on the API side.

Microsoft redirect URIs must be registered under **SPA**, not **Web**.

---

## License

MIT — see [LICENSE.txt](LICENSE.txt).
