# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Letmework4U is a job application tracking system built with:
- **Blazor WebAssembly** (Frontend) - .NET 8
- **Azure Functions** (Backend API) - Isolated Worker Model
- **Entity Framework Core** with Azure Cosmos DB
- **Microsoft Entra External ID** for authentication (OAuth 2.0/MSAL)
- **Azure Communication Services** for email reminders

The architecture has been refactored from a repository pattern to direct EF Core usage in Azure Functions with JWT-based authentication.

## Build and Run Commands

### Running Locally

**Start Azure Functions (Backend):**
```bash
cd Letmework4U.Functions
func start
```
API runs at `http://localhost:7071`

**Start Blazor Client (Frontend):**
```bash
cd Letmework4U.Client
dotnet run
```
App runs at `https://localhost:5001`

### Build
```bash
dotnet build
dotnet build -c Release
```

### Testing (when tests are added)
```bash
dotnet test
```

## Project Structure

```
Letmework4U/
├── Letmework4U.Client/              # Blazor WebAssembly frontend
│   ├── Pages/                       # Razor pages/components
│   ├── Services/                    # HTTP API clients
│   ├── Program.cs                   # MSAL authentication setup
│   └── wwwroot/appsettings.json     # Client config (Entra ID)
├── Letmework4U.Functions/           # Azure Functions backend
│   ├── Functions/                   # HTTP-triggered CRUD functions
│   ├── Services/                    # MappingService, EmailService
│   ├── Program.cs                   # JWT auth & EF Core setup
│   └── local.settings.json          # Backend config (secrets)
├── Letmework4U.Infrastructure/      # EF Core data access
│   ├── Entities/                    # Entity models
│   └── ApplicationDbContext.cs      # DbContext with Cosmos config
└── Letmework4U.Shared/             # DTOs and enums
    └── DTO.cs                       # All DTOs
```

## Architecture

### Authentication Flow
1. User authenticates via Microsoft Entra External ID (MSAL) in Blazor client
2. MSAL acquires JWT access token with API scopes
3. HttpClient automatically attaches token to all API requests
4. Azure Functions validate JWT and extract `UserId` claim (from `sub` or `oid`)
5. All data operations are filtered by `UserId` for user isolation

### Data Model
- **Cosmos DB Containers**: Each entity type has its own container with specific partition keys
- **Partition Keys**:
  - `Applicants`, `Companies`, `Recruiters` → partitioned by `UserId`
  - `JobApplications`, `Meetings`, `ApplicantTasks` → partitioned by `ApplicantId`
  - `InterviewSteps`, `JobApplicationRecruiters` → partitioned by `JobApplicationId`
  - `MeetingRecruiters` → partitioned by `MeetingId`
- **Key Relationships**:
  - `Applicant` (1) → (∞) `JobApplications`, `Meetings`, `Tasks`
  - `JobApplication` (∞) ← (∞) `Recruiters` (via `JobApplicationRecruiter` join table)
  - `Meeting` (∞) ← (∞) `Recruiters` (via `MeetingRecruiter` join table)

### Azure Functions Pattern
All CRUD functions follow this pattern:
1. Extract `userId` from JWT token claims
2. Find/verify `Applicant` record by `UserId`
3. Filter queries by `ApplicantId` for data isolation
4. Use `MappingService` for Entity ↔ DTO conversions
5. Return appropriate HTTP status codes

## Important Implementation Details

### EF Core with Cosmos DB
- **No migrations**: Cosmos containers are created automatically based on `ApplicationDbContext.OnModelCreating()`
- **Partition key must be in entity**: Every query/write must include the partition key
- **Use `.Include()`** for related data (not lazy loading)
- **Soft deletes**: Use `IsDeleted` flag, don't physically delete records

### MSAL Authentication in Blazor
- Token acquisition is automatic via `AuthorizationMessageHandler`
- Configure scopes in `wwwroot/appsettings.json` under `ApiScopes`
- Use `<AuthorizeView>` in Razor components for conditional rendering
- Authentication redirect pages at `/authentication/{action}`

### Email Reminders (Timer Functions)
- **InterviewReminderFunction**: Runs every hour (`0 0 * * * *`)
  - Checks `Meeting.SendReminderEmail` and `ReminderMinutesBefore`
  - Updates `ReminderSent` flag after sending
- **TaskReminderFunction**: Runs daily at 8 AM UTC (`0 0 8 * * *`)
  - Sends reminders for tasks due today
  - Updates `ReminderSent` flag after sending

### Configuration Files
**DO NOT COMMIT** actual secrets to `local.settings.json` or `appsettings.json`

Required configuration:
- `CosmosDb__ConnectionString` - Cosmos DB connection
- `MicrosoftEntraExternalId__*` - Entra ID tenant details
- `AzureCommunicationServices__*` - Email service credentials

## Common Development Patterns

### Adding a New Entity
1. Create entity class in `Letmework4U.Infrastructure/Entities/`
2. Add DbSet to `ApplicationDbContext.cs`
3. Configure in `OnModelCreating()` with container, partition key, relationships
4. Create DTO in `Letmework4U.Shared/DTO.cs`
5. Add mapping methods to `MappingService.cs` (ToDto/ToEntity)
6. Create Azure Functions in `Letmework4U.Functions/Functions/`
7. Create service interface/implementation in `Letmework4U.Client/Services/`

### Querying Related Data
Always use `.Include()` to avoid N+1 queries:
```csharp
var jobApps = await _context.JobApplications
    .Where(ja => ja.ApplicantId == applicantId && !ja.IsDeleted)
    .Include(ja => ja.Company)
    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
    .Include(ja => ja.InterviewSteps)
    .Include(ja => ja.ApplicantTasks)
    .ToListAsync();
```

### User Isolation Pattern
Every Azure Function must:
1. Extract userId from claims:
   ```csharp
   var userId = req.HttpContext.User.FindFirst("sub")?.Value
                ?? req.HttpContext.User.FindFirst("oid")?.Value;
   ```
2. Get applicant record:
   ```csharp
   var applicant = await _context.Applicants
       .FirstOrDefaultAsync(a => a.UserId == userId);
   ```
3. Filter all queries by `ApplicantId`

## Enums

### JobApplicationStatus
`NotApplied`, `Applied`, `FirstInterview`, `Meetings`, `Rejected`, `Offer`, `Completed`, `NoResponse`

### TaskType
`FollowUp`, `ReachOut`, `SendAvailability`, `PrepareCoverLetter`, `PrepareResume`, `PrepareCVDetailed`, `PrepareForInterview`, `Apply`, `CompleteExercise`, `EnglishTest`, `VideoIntroduction`

### JobSource
`LinkedIn`, `Indeed`, `PersonalConnection`, `CompanyWebsite`, `Recruiter`, `Referral`, `Other`

## Key API Endpoints

All endpoints are under `/api/` and require JWT authentication.

- **Companies**: `/api/Companies`, `/api/Companies/CompanyGetById/{id}`, `/api/Companies/CompanyAdd`
- **Recruiters**: `/api/Recruiters`, `/api/Recruiters/RecruiterGetById/{id}`, `/api/Recruiters/RecruiterAdd`
- **JobApplications**: `/api/JobApplications`, `/api/JobApplications/board`, `/api/JobApplications/{id}/status`
- **Meetings**: `/api/Meetings`, `/api/Meetings/MeetingGetById/{id}`, `/api/Meetings/MeetingAdd`
- **Tasks**: `/api/ApplicantTasks`, `/api/ApplicantTasks/ApplicantTaskGetById/{id}`

## Known Issues and Migration Notes

- **Repository pattern removed**: Old services in `Letmework4U.Client/Services/` may contain legacy code
- **StateContainer**: Consider migrating to `AuthenticationState` for user state
- **CV Upload**: Blob Storage integration planned but not yet implemented
- **Many-to-many relationships**: Handled via explicit join entities (`JobApplicationRecruiter`, `MeetingRecruiter`)

## Azure Resources Required

1. **Microsoft Entra External ID Tenant**
   - SPA app registration for Blazor client
   - API app registration for Azure Functions
   - User flow: Sign up and sign in (susi)
   - API scope: `user_impersonation`

2. **Azure Cosmos DB** (SQL API) - Database: `Letmework4U`

3. **Azure Communication Services** - For email reminders

4. **Azure Functions** - Consumption or Premium plan

5. **Azure Static Web Apps** or similar - For Blazor hosting