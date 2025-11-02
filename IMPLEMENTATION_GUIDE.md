# Letmework4U - Implementation Guide

## Overview
This guide covers the complete migration from repository pattern to EF Core direct access, Azure Functions backend implementation with Microsoft Entra External ID authentication, and Blazor client MSAL integration.

## Recent Updates (2025-10-26)

### Configuration Completed ✅
**What was accomplished:**
1. **Retrieved Azure resource values** using Azure CLI:
   - Cosmos DB connection string from `letmework4udb` resource
   - Azure subscription and tenant information
   - Function App URL: `letmework4ufunctions.azurewebsites.net`

2. **Updated local.settings.json** with real Azure values:
   - Cosmos DB connection string and database name
   - Microsoft Entra External ID tenant, client ID, and audience
   - All Entra ID configuration values retrieved and set

3. **Configured environment-specific settings**:
   - Production: `appsettings.json` points to Azure Function App
   - Development: `appsettings.Development.json` points to local Functions (localhost:7071)

4. **Enhanced Program.cs** with:
   - Environment detection and logging
   - Automatic config file selection
   - Improved HttpClient configuration with 30-second timeout
   - Console logging for API URL and scopes debugging

5. **Identified existing Azure resources**:
   - Entra ID tenant: `letmework4u.ciamlogin.com`
   - Cosmos DB: `letmework4udb` in `rgCap`
   - Function App: `Letmework4UFunctions` in `rgSwitzerland`
   - Storage Account: `letmework4ustorage` in `rgCap`

### Still Needed ⚠️
- Azure Communication Services resource (for email reminders)
- Blob Storage container setup (for CV uploads)

## What Has Been Completed

### 1. Entity Models ✅
Created all entity models in `Letmework4U.Infrastructure/Entities/`:
- `Applicant.cs` - User profile with UserId for Microsoft Entra External ID
- `Company.cs` - Company information
- `Recruiter.cs` - Recruiter contacts
- `JobApplication.cs` - Job applications with relationships
- `Meeting.cs` - Interview meetings with reminder tracking
- `ApplicantTask.cs` - Tasks with reminder tracking
- `CVVersion.cs`, `JobReference.cs`, `InterviewStep.cs`
- `JobApplicationRecruiter.cs`, `MeetingRecruiter.cs` - Many-to-many join tables

### 2. DbContext Implementation ✅
**File:** `Letmework4U.Infrastructure/ApplicationDbContext.cs`
- Configured for Azure Cosmos DB
- All DbSets defined
- Proper partition keys and relationships
- Container-based data model

### 3. .NET 8 Upgrade ✅
All projects upgraded to .NET 8:
- `Letmework4U.Client` - Blazor WebAssembly
- `Letmework4U.Shared` - DTOs and Enums
- `Letmework4U.Core` - Business logic (currently empty)
- `Letmework4U.Infrastructure` - Data access
- `Letmework4U.Functions` - NEW Azure Functions project

### 4. Azure Functions Project ✅
**Location:** `Letmework4U.Functions/`

**Dependencies installed:**
- Microsoft.Azure.Functions.Worker (2.1.0)
- Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore (2.0.2)
- Microsoft.Azure.Functions.Worker.Extensions.Timer (4.3.1)
- Microsoft.EntityFrameworkCore.Cosmos (8.0.10)
- Microsoft.Identity.Web (3.3.2)
- Azure.Communication.Email (1.1.0)

**Services created:**
- `MappingService.cs` - Entity ↔ DTO conversions
- `EmailService.cs` - Azure Communication Services email
- `AuthHelper.cs` - JWT token extraction and validation

**Program.cs configured with:**
- Microsoft Entra External ID JWT authentication
- EF Core with Cosmos DB
- Email service registration
- Application Insights

###  5. CRUD Functions Implemented ✅
- **CompanyFunctions.cs** - Full CRUD with user isolation
- **RecruiterFunctions.cs** - Full CRUD with user isolation
- **ApplicantTaskFunctions.cs** - Full CRUD with user isolation
- **MeetingFunctions.cs** - Full CRUD with recruiter associations

## Remaining Implementation Tasks

### 6. JobApplication Functions
**File to create:** `Letmework4U.Functions/Functions/JobApplicationFunctions.cs`

**Endpoints needed:**
```csharp
[Function("JobApplications")] GET /api/JobApplications
[Function("JobApplicationGetById")] GET /api/JobApplications/JobApplicationGetById/{id}
[Function("JobApplicationAdd")] POST /api/JobApplications/JobApplicationAdd
[Function("JobApplicationUpdate")] PATCH /api/JobApplications/{id}
[Function("JobApplicationDeleteById")] DELETE /api/JobApplications/JobApplicationDeleteById/{id}
[Function("JobApplicationUpdateStatus")] PATCH /api/JobApplications/{id}/status
[Function("JobApplicationBoard")] GET /api/JobApplications/board
```

**Key implementation notes:**
- Include Company, Recruiters, InterviewSteps, ApplicantTasks in queries
- Use `.Include()` for related data
- Map many-to-many JobApplicationRecruiter relationships
- Filter by ApplicantId (derived from authenticated user)
- For Board endpoint: group by Status into BoardColumnDto

**Sample GetAll implementation:**
```csharp
var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
var jobApps = await _context.JobApplications
    .Where(ja => ja.ApplicantId == applicant.Id && !ja.IsDeleted)
    .Include(ja => ja.Company)
    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
    .Include(ja => ja.InterviewSteps)
    .Include(ja => ja.ApplicantTasks)
    .ToListAsync();

var jobAppsDto = jobApps.Select(ja =>
{
    var recruiters = ja.JobApplicationRecruiters.Select(jar => jar.Recruiter).ToList();
    return ja.ToDto(ja.Company, recruiters);
}).ToList();
```

### 7. Applicant Profile Functions
**File to create:** `Letmework4U.Functions/Functions/ApplicantFunctions.cs`

**Endpoints needed:**
```csharp
[Function("ApplicationGetById")] GET /api/Applicants/ApplicationGetById/{id}
[Function("ApplicantUpdate")] PATCH /api/Applicants/{id}
[Function("UploadCV")] POST /api/Applicants/UploadCV
[Function("AddReference")] POST /api/Applicants/AddReference
[Function("DeleteReference")] DELETE /api/Applicants/DeleteReference/{referenceId}
```

**Key implementation notes:**
- Get applicant by UserId, not by Id parameter
- CV upload should save to Azure Blob Storage (connection string in config)
- Return BlobUrl in CVVersion entity
- AddReference creates JobReference entity
- Auto-create Applicant on first login if not exists

### 8. Timer Functions for Reminders

#### Interview Reminder Function
**File to create:** `Letmework4U.Functions/Functions/InterviewReminderFunction.cs`

```csharp
[Function("InterviewReminder")]
public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer) // Every hour
{
    var now = DateTime.UtcNow;
    var meetings = await _context.Meetings
        .Where(m => !m.ReminderSent && m.SendReminderEmail &&
                    m.StartDateTime > now &&
                    m.StartDateTime <= now.AddMinutes(m.ReminderMinutesBefore))
        .Include(m => m.Applicant)
        .ToListAsync();

    foreach (var meeting in meetings)
    {
        var subject = $"Interview Reminder: {meeting.Title}";
        var htmlContent = $@"
            <h2>Upcoming Interview</h2>
            <p><strong>Title:</strong> {meeting.Title}</p>
            <p><strong>Start Time:</strong> {meeting.StartDateTime:yyyy-MM-dd HH:mm}</p>
            <p><strong>Description:</strong> {meeting.Description}</p>
            {(meeting.IsPhonecall ? $"<p><strong>Phone Call</strong></p>" : "")}
            {(!string.IsNullOrEmpty(meeting.OnlineMeetingURL) ? $"<p><strong>Meeting Link:</strong> <a href='{meeting.OnlineMeetingURL}'>{meeting.OnlineMeetingURL}</a></p>" : "")}
            {(!string.IsNullOrEmpty(meeting.PhysicalLocation) ? $"<p><strong>Location:</strong> {meeting.PhysicalLocation}</p>" : "")}
            {(!string.IsNullOrEmpty(meeting.MeetingAccessCode) ? $"<p><strong>Access Code:</strong> {meeting.MeetingAccessCode}</p>" : "")}
        ";

        await _emailService.SendEmailAsync(meeting.Applicant.ContactEmail, subject, htmlContent);

        meeting.ReminderSent = true;
    }

    await _context.SaveChangesAsync();
    _logger.LogInformation($"Sent {meetings.Count} interview reminders");
}
```

#### Task Reminder Function
**File to create:** `Letmework4U.Functions/Functions/TaskReminderFunction.cs`

```csharp
[Function("TaskReminder")]
public async Task Run([TimerTrigger("0 0 8 * * *")] TimerInfo timer) // Daily at 8 AM UTC
{
    var today = DateTime.UtcNow.Date;
    var tasks = await _context.ApplicantTasks
        .Where(t => !t.ReminderSent && !t.Completed && t.Deadline.Date == today)
        .Include(t => t.Applicant)
        .Include(t => t.JobApplication)
        .ToListAsync();

    foreach (var task in tasks)
    {
        var subject = $"Task Due Today: {task.Title}";
        var htmlContent = $@"
            <h2>Task Reminder</h2>
            <p><strong>Title:</strong> {task.Title}</p>
            <p><strong>Type:</strong> {task.Type}</p>
            <p><strong>Deadline:</strong> {task.Deadline:yyyy-MM-dd HH:mm}</p>
            <p><strong>Note:</strong> {task.Note}</p>
            {(task.JobApplication != null ? $"<p><strong>Related to:</strong> {task.JobApplication.JobTitle}</p>" : "")}
        ";

        await _emailService.SendEmailAsync(task.Applicant.ContactEmail, subject, htmlContent);

        task.ReminderSent = true;
    }

    await _context.SaveChangesAsync();
    _logger.LogInformation($"Sent {tasks.Count} task reminders");
}
```

### 9. Blazor Client - MSAL Authentication ✅

#### Configuration Files - COMPLETED
**Files:**
- `Letmework4U.Client/wwwroot/appsettings.json` (Production)
- `Letmework4U.Client/wwwroot/appsettings.Development.json` (Local Development)

**Production (appsettings.json):**
```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com",
    "ClientId": "37b37fa2-c0c4-4766-8bc7-1478524d359d",
    "ValidateAuthority": false
  },
  "ApiBaseUrl": "https://letmework4ufunctions.azurewebsites.net/api/",
  "ApiScopes": [
    "api://f0788507-bdab-4aa8-bf96-857dda053319/user_impersonation"
  ]
}
```

**Development (appsettings.Development.json):**
```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com",
    "ClientId": "37b37fa2-c0c4-4766-8bc7-1478524d359d",
    "ValidateAuthority": false
  },
  "ApiBaseUrl": "http://localhost:7071/api/",
  "ApiScopes": [
    "api://f0788507-bdab-4aa8-bf96-857dda053319/user_impersonation"
  ]
}
```

#### Program.cs - COMPLETED ✅
**File:** `Letmework4U.Client/Program.cs`

Implementation includes:

```csharp
using Microsoft.Authentication.WebAssembly.Msal;

// Environment detection and logging
var environment = builder.HostEnvironment.Environment;
Console.WriteLine($"Running in environment: {environment}");

// Add MSAL authentication
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("MicrosoftEntraExternalId", options.ProviderOptions.Authentication);

    // Add API scopes for access tokens
    var apiScopes = builder.Configuration.GetSection("ApiScopes").Get<string[]>();
    if (apiScopes != null)
    {
        foreach (var scope in apiScopes)
        {
            options.ProviderOptions.DefaultAccessTokenScopes.Add(scope);
        }
    }
});

// Configure HttpClient with authentication and environment-specific base URL
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:7071/api/";
var apiScopes = builder.Configuration.GetSection("ApiScopes").Get<string[]>() ?? [];

Console.WriteLine($"API Base URL: {apiBaseUrl}");
Console.WriteLine($"API Scopes: {string.Join(", ", apiScopes)}");

builder.Services.AddHttpClient("Letmework4U.API", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler(sp =>
{
    var handler = sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(
            authorizedUrls: new[] { apiBaseUrl },
            scopes: apiScopes
        );
    return handler;
});

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
    .CreateClient("Letmework4U.API"));
```

**Key Features:**
- ✅ Environment detection (Development/Production)
- ✅ Automatic config file selection based on environment
- ✅ Console logging for debugging configuration
- ✅ 30-second HTTP timeout
- ✅ Proper scope configuration from appsettings
- ✅ Authenticated HttpClient with automatic token attachment

#### Update App.razor
Add authentication routes:

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    @if (context.User.Identity?.IsAuthenticated != true)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        <p role="alert">You are not authorized to access this resource.</p>
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
    </Router>
</CascadingAuthenticationState>
```

#### Add Authentication Components
**File:** `Letmework4U.Client/Pages/Authentication.razor`

```razor
@page "/authentication/{action}"
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

<RemoteAuthenticatorView Action="@Action" />

@code {
    [Parameter] public string? Action { get; set; }
}
```

#### Update NavMenu with LoginDisplay
Add to MainLayout or NavMenu:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject NavigationManager Navigation

<AuthorizeView>
    <Authorized>
        <span>Hello, @context.User.Identity?.Name!</span>
        <button @onclick="BeginSignOut">Log out</button>
    </Authorized>
    <NotAuthorized>
        <a href="authentication/login">Log in</a>
    </NotAuthorized>
</AuthorizeView>

@code {
    private void BeginSignOut()
    {
        Navigation.NavigateTo("authentication/logout");
    }
}
```

### 10. Configuration Files ✅

#### Azure Functions - local.settings.json - COMPLETED
**File:** `Letmework4U.Functions/local.settings.json`

**Current Configuration (Retrieved from Azure):**

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__ConnectionString": "AccountEndpoint=https://letmework4udb.documents.azure.com:443/;AccountKey=***;",
    "CosmosDb__DatabaseName": "letmework4UCosmos",
    "MicrosoftEntraExternalId__Instance": "https://letmework4u.ciamlogin.com/",
    "MicrosoftEntraExternalId__TenantId": "6ef4c7b2-8fee-4a0a-bfc6-4dcaaacc92ef",
    "MicrosoftEntraExternalId__ClientId": "f0788507-bdab-4aa8-bf96-857dda053319",
    "MicrosoftEntraExternalId__Audience": "api://f0788507-bdab-4aa8-bf96-857dda053319",
    "AzureCommunicationServices__ConnectionString": "YOUR_ACS_CONNECTION_STRING",
    "AzureCommunicationServices__SenderAddress": "DoNotReply@YOUR_DOMAIN.com"
  }
}
```

**Configuration Status:**
- ✅ Cosmos DB Connection String - Retrieved from Azure resource `letmework4udb` in `rgCap`
- ✅ Cosmos DB Database Name - Set to `letmework4UCosmos`
- ✅ Entra ID Instance - Set to `https://letmework4u.ciamlogin.com/`
- ✅ Entra ID Tenant ID - Retrieved from Azure subscription
- ✅ Entra ID Client ID - API app registration ID
- ✅ Entra ID Audience - API scope
- ⚠️ Azure Communication Services - Not yet configured (resource needs to be created)

**Note:** Azure Communication Services resource needs to be created for email reminders to work.

## Azure Setup Requirements

### 1. Microsoft Entra External ID Tenant ✅
- ✅ Created Microsoft Entra External ID tenant: `letmework4u.ciamlogin.com`
- ✅ Registered application for Blazor client (SPA): Client ID `37b37fa2-c0c4-4766-8bc7-1478524d359d`
- ✅ Registered application for API (Azure Functions): Client ID `f0788507-bdab-4aa8-bf96-857dda053319`
- ✅ User flows configured: Sign up and sign in
- ✅ API scope exposed: `api://f0788507-bdab-4aa8-bf96-857dda053319/user_impersonation`
- ✅ Blazor app granted permission to API scope

**Tenant Information:**
- **Authority**: `https://letmework4u.ciamlogin.com/letmework4u.onmicrosoft.com`
- **Tenant ID**: `6ef4c7b2-8fee-4a0a-bfc6-4dcaaacc92ef`
- **Subscription**: Azure subscription 1 (ID: `c3b5812f-459b-4ce1-8677-c9b117764039`)

### 2. Azure Cosmos DB ✅
- ✅ Created Cosmos DB account (SQL API): `letmework4udb`
- ✅ Resource Group: `rgCap`
- ✅ Endpoint: `https://letmework4udb.documents.azure.com:443/`
- ✅ Database: `letmework4UCosmos`
- ✅ Containers auto-created by EF Core based on `ApplicationDbContext` configuration

### 3. Azure Communication Services ⚠️
**Status:** NOT YET CREATED - Required for email reminders

**To Complete:**
1. Create ACS resource in Azure Portal
2. Get connection string
3. Add verified email domain or use Azure Managed Domain
4. Configure sender address
5. Update `local.settings.json` with connection string and sender address

### 4. Azure Functions ✅
- ✅ Function App Created: `Letmework4UFunctions`
- ✅ Resource Group: `rgSwitzerland`
- ✅ URL: `https://letmework4ufunctions.azurewebsites.net`
- ✅ Status: Running

### 5. Azure Blob Storage (for CV uploads) 📋
**Status:** PLANNED - Storage account exists but not yet integrated

**Existing Resource:**
- Storage Account: `letmework4ustorage` (in `rgCap`)

**To Complete:**
1. Create container: `cvs`
2. Set public access level or use SAS tokens
3. Update `local.settings.json` with Blob Storage connection string
4. Implement CV upload functionality in ApplicantFunctions

## Running the Application

### 1. Start Azure Functions
```bash
cd Letmework4U.Functions
func start
```

### 2. Start Blazor Client
```bash
cd Letmework4U.Client
dotnet run
```

## Testing Authentication

1. Navigate to Blazor app (usually https://localhost:5001)
2. Click "Log in" - redirects to Microsoft Entra External ID
3. Sign up or sign in
4. After authentication, token is automatically included in API calls
5. Azure Functions validate JWT token and extract UserId
6. Data is filtered by UserId for complete user isolation

## Security Features Implemented

✅ **Microsoft Entra External ID Integration** - Industry-standard OAuth 2.0/OpenID Connect
✅ **JWT Token Authentication** - All API endpoints validate tokens
✅ **User Isolation** - Data filtered by UserId from JWT claims
✅ **Automatic Token Refresh** - MSAL handles token lifecycle
✅ **Multi-tenant Ready** - Each user has isolated data via UserId
✅ **Password Reset & Profile Edit** - Built into Microsoft Entra External ID user flows

## Database Schema (Cosmos DB)

### Containers & Partition Keys
- **Applicants** - Partition Key: `UserId`
- **Companies** - Partition Key: `UserId`
- **Recruiters** - Partition Key: `UserId`
- **JobApplications** - Partition Key: `ApplicantId`
- **Meetings** - Partition Key: `ApplicantId`
- **ApplicantTasks** - Partition Key: `ApplicantId`
- **CVVersions** - Partition Key: `ApplicantId`
- **JobReferences** - Partition Key: `ApplicantId`
- **InterviewSteps** - Partition Key: `JobApplicationId`
- **JobApplicationRecruiters** - Partition Key: `JobApplicationId`
- **MeetingRecruiters** - Partition Key: `MeetingId`

## API Endpoints Summary

### Companies
- `GET /api/Companies` - Get all companies for user
- `GET /api/Companies/CompanyGetById/{id}` - Get company by ID
- `POST /api/Companies/CompanyAdd` - Create company
- `PATCH /api/Companies/{id}` - Update company
- `DELETE /api/Companies/{id}` - Delete company

### Recruiters
- `GET /api/Recruiters` - Get all recruiters for user
- `GET /api/Recruiters/RecruiterGetById/{id}` - Get recruiter by ID
- `POST /api/Recruiters/RecruiterAdd` - Create recruiter
- `PATCH /api/Recruiters/{id}` - Update recruiter
- `DELETE /api/Recruiters/RecruiterDeleteById/{id}` - Delete recruiter

### Applicant Tasks
- `GET /api/ApplicantTasks` - Get all tasks for user
- `GET /api/ApplicantTasks/ApplicantTaskGetById/{id}` - Get task by ID
- `POST /api/ApplicantTasks/ApplicantTaskAdd` - Create task
- `PATCH /api/ApplicantTasks/{id}` - Update task
- `DELETE /api/ApplicantTasks/ApplicantTaskDeleteById/{id}` - Delete task

### Meetings
- `GET /api/Meetings` - Get all meetings for user
- `GET /api/Meetings/MeetingGetById/{id}` - Get meeting by ID
- `POST /api/Meetings/MeetingAdd` - Create meeting
- `PATCH /api/Meetings/{id}` - Update meeting
- `DELETE /api/Meetings/MeetingDeleteById/{id}` - Delete meeting

### Job Applications (to be implemented)
- `GET /api/JobApplications` - Get all applications for user
- `GET /api/JobApplications/JobApplicationGetById/{id}` - Get application by ID
- `POST /api/JobApplications/JobApplicationAdd` - Create application
- `PATCH /api/JobApplications/{id}` - Update application
- `DELETE /api/JobApplications/JobApplicationDeleteById/{id}` - Delete application
- `GET /api/JobApplications/board` - Get board view (grouped by status)

### Applicants (to be implemented)
- `GET /api/Applicants/ApplicationGetById/{id}` - Get current user profile
- `PATCH /api/Applicants/{id}` - Update profile
- `POST /api/Applicants/UploadCV` - Upload CV
- `POST /api/Applicants/AddReference` - Add job reference
- `DELETE /api/Applicants/DeleteReference/{referenceId}` - Delete reference

### Timer Functions (automated)
- Interview Reminder - Runs every hour
- Task Reminder - Runs daily at 8 AM UTC

## Next Steps

1. Implement remaining JobApplicationFunctions.cs
2. Implement ApplicantFunctions.cs with Blob Storage integration
3. Create InterviewReminderFunction.cs
4. Create TaskReminderFunction.cs
5. Configure Microsoft Entra External ID tenant and get credentials
6. Set up Cosmos DB and get connection string
7. Set up Azure Communication Services for emails
8. Update all configuration files with real values
9. Test end-to-end authentication flow
10. Deploy to Azure

## Migration from Old Code

The old repository pattern services in `Letmework4U.Client/Services/` are now replaced by:
- Direct HTTP calls to Azure Functions
- Authentication tokens automatically attached via MSAL
- No need for StateContainer pattern (use AuthenticationState instead)

You can delete or archive old service files once new implementation is tested.
