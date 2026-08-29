# Local setup

From a clean clone to a running app. Every command here was run against a fresh clone; where something is easy to get wrong, it says so.

## Prerequisites

| | |
|---|---|
| .NET 8 SDK | `dotnet --version` should report 8.x or later |
| Node.js | Only to run Azurite via `npx` |
| Azure Functions Core Tools v4 | Only if you want to run the reminder jobs |

**No Cosmos DB emulator.** Local development uses the EF InMemory provider. The emulator needs UAC elevation and adds nothing here — the Cosmos-specific behaviour that matters is verified against a real account, not an emulator.

## 1. Clone and build

```bash
git clone <repo-url> MiFuturoTrabajo
```

```bash
cd MiFuturoTrabajo && dotnet build MiFuturoTrabajo.sln
```

## 2. Set the JWT signing key

The API refuses to start without it. This is the only secret needed for local development.

```bash
dotnet user-secrets --project MiFuturoTrabajo.Api set "Jwt:SecretKey" "$(openssl rand -base64 48)"
```

On Windows without `openssl`:

```powershell
dotnet user-secrets --project MiFuturoTrabajo.Api set "Jwt:SecretKey" ([Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 })))
```

User-secrets are stored outside the repository, so they cannot be committed by accident. `appsettings.Development.json` is committed and deliberately contains **no** secret — the `Jwt:SecretKey` line is absent, not blank, so a missing key fails loudly rather than silently using an empty one.

## 3. Start Azurite

Blob storage for CV uploads.

```bash
npx azurite
```

Leave it running. `appsettings.Development.json` already points at it with `UseDevelopmentStorage=true`.

Skip this only if you will not touch CV upload — the API starts fine without it and fails at upload time.

## 4. Run

Two terminals.

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

The client's `wwwroot/appsettings.Development.json` already points at `https://localhost:7071/api/`.

## 5. Register an account

Sign up through the UI. There is no seeded account locally — the demo seeder only runs when `Demo:Enabled` is true, which it is not in development.

**Verification and password-reset links go to the API log, not to an inbox.** With no mail provider configured, `NoOpEmailService` extracts the link and writes it out:

```
warn: [no email configured] Would have sent "Verify your email - Mi Futuro Trabajo"
      to you@example.com.
      Link: https://localhost:5011/verify-email?token=5Mak78lG...&email=you@example.com
```

Paste that into the browser. The token is deliberately **not** returned in any HTTP response, in any environment — a habit that only had to survive one environment misconfiguration to become anonymous account takeover.

## Running against real Cosmos

Only needed to verify Cosmos-specific behaviour; day-to-day work does not require it.

```bash
dotnet user-secrets --project MiFuturoTrabajo.Api set "Persistence:Provider" "Cosmos"
```

```bash
dotnet user-secrets --project MiFuturoTrabajo.Api set "CosmosDb:ConnectionString" "<connection string>"
```

Containers are created at startup. The database name defaults to `MiFuturoTrabajo` — override `CosmosDb:DatabaseName` to keep an experiment separate from anything you care about.

## Running the reminder functions

```bash
cd MiFuturoTrabajo.Functions
```

```bash
cp local.settings.json.example local.settings.json
```

Fill in `CosmosDb:ConnectionString`. There is **no InMemory option** here on purpose: a reminder host pointed at an empty throwaway database reads nothing and mails nobody, which looks exactly like working correctly.

```bash
func start
```

Both timers appear at startup. To see one fire without waiting for 08:00 UTC, set the schedules in `local.settings.json` to something like `*/20 * * * * *`.

`local.settings.json` is gitignored — it holds connection strings.

## Tests

```bash
dotnet test
```

196 tests. No emulator, no storage account, no network: the suite ships `appsettings.Testing.json`, an in-memory blob store and a capturing mail service. If a test needs a real dependency, that is a bug in the test.

## Troubleshooting

**`JWT SecretKey is not configured`** — step 2 was skipped, or run from a different directory. `dotnet user-secrets --project MiFuturoTrabajo.Api list` should show it.

**CV upload returns 500** — Azurite is not running.

**Client shows no data, browser console shows CORS errors** — the API is not on 7071, or is running in an environment where `localhost` is not in the allowlist. Outside Production the two localhost origins are added automatically; in Production they are not, deliberately.

**`func start` reports 0 functions** — build first. The isolated worker generates `functions.metadata` at build time, and without it the host has nothing to index.

**`Filename too long` while cloning, on Windows** — the Bootstrap source-map files in `wwwroot/lib` are long enough to cross `MAX_PATH` when the clone sits in a deeply nested directory. Clone somewhere short (`C:\src\MiFuturoTrabajo`), or:

```bash
git config --global core.longpaths true
```
