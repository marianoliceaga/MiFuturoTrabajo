# MiFuturoTrabajo.Api

ASP.NET Core Web API for the MiFuturoTrabajo job application tracking system. This project is a migration from Azure Functions to a standard Web API that can run on a free Azure App Service.

## Project Overview

This is a fully-functional ASP.NET Core 8.0 Web API that provides all the backend functionality for the MiFuturoTrabajo application.

### Architecture

- **Framework**: ASP.NET Core 8.0 Web API
- **Authentication**: Custom JWT-based authentication
- **Database**: Azure Cosmos DB (via Entity Framework Core)
- **Email Service**: Azure Communication Services
- **Deployment Target**: Azure App Service (Free/Basic tier compatible)

## Controllers

The API includes the following controllers:

1. **AuthController** (`/api/auth`) - User authentication and management
   - POST `/register` - Register new user
   - POST `/login` - User login
   - POST `/refresh` - Refresh access token
   - GET `/me` - Get current user profile
   - POST `/send-verification` - Send email verification
   - POST `/verify-email` - Verify email with token
   - POST `/forgot-password` - Request password reset
   - POST `/reset-password` - Reset password with token
   - POST `/change-password` - Change password (authenticated)

2. **CompanyController** (`/api/company`) - Company management
   - GET `/` - Get all companies
   - GET `/CompanyGetById/{id}` - Get company by ID
   - POST `/CompanyAdd` - Create company
   - PATCH `/{id}` - Update company
   - DELETE `/{id}` - Delete company

3. **RecruiterController** (`/api/recruiter`) - Recruiter management
   - GET `/` - Get all recruiters
   - GET `/RecruiterGetById/{id}` - Get recruiter by ID
   - POST `/RecruiterAdd` - Create recruiter
   - PATCH `/{id}` - Update recruiter
   - DELETE `/RecruiterDeleteById/{id}` - Delete recruiter

4. **JobApplicationController** (`/api/jobapplication`) - Job application management
   - GET `/` - Get all job applications
   - GET `/board` - Get job applications grouped by status (Kanban board)
   - GET `/JobApplicationGetById/{id}` - Get job application by ID
   - POST `/JobApplicationAdd` - Create job application
   - PATCH `/{id}` - Update job application
   - PATCH `/{id}/status` - Update job application status
   - DELETE `/JobApplicationDeleteById/{id}` - Soft delete job application

5. **MeetingController** (`/api/meeting`) - Interview/meeting management
   - GET `/` - Get all meetings
   - GET `/MeetingGetById/{id}` - Get meeting by ID
   - POST `/MeetingAdd` - Create meeting
   - PATCH `/{id}` - Update meeting
   - DELETE `/MeetingDeleteById/{id}` - Delete meeting

6. **ApplicantTaskController** (`/api/applicanttask`) - Task management
   - GET `/` - Get all tasks
   - GET `/ApplicantTaskGetById/{id}` - Get task by ID
   - POST `/ApplicantTaskAdd` - Create task
   - PATCH `/{id}` - Update task
   - DELETE `/ApplicantTaskDeleteById/{id}` - Delete task

7. **ApplicantController** (`/api/applicant`) - Applicant profile management
   - GET `/ApplicationGetById/{id}` - Get applicant by ID
   - GET `/current` - Get current logged-in applicant (auto-creates if needed)
   - PATCH `/{id}` - Update applicant profile
   - POST `/UploadCV` - Upload CV version
   - PATCH `/CV/{cvId}/setDefault` - Set default CV
   - DELETE `/CV/{cvId}` - Delete CV version
   - POST `/AddReference` - Add job reference
   - DELETE `/DeleteReference/{referenceId}` - Delete job reference

## Services

### Authentication Services
- **JwtService** - JWT token generation and validation
- **PasswordHasher** - BCrypt password hashing

### Business Services
- **EmailService** - Azure Communication Services integration
- **MappingService** - Entity to DTO mapping extensions

## Configuration

### Required App Settings

Create an `appsettings.json` or configure environment variables with the following:

```json
{
  "Jwt": {
    "SecretKey": "YourSecretKeyMustBeAtLeast32CharactersLong!",
    "Issuer": "MiFuturoTrabajo",
    "Audience": "MiFuturoTrabajo.Client",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 7
  },
  "CosmosDb": {
    "ConnectionString": "YOUR_COSMOS_DB_CONNECTION_STRING",
    "DatabaseName": "MiFuturoTrabajo"
  },
  "AzureCommunicationServices": {
    "ConnectionString": "YOUR_ACS_CONNECTION_STRING",
    "SenderAddress": "DoNotReply@your-domain.com"
  },
  "BlazorClient": {
    "Url": "https://localhost:5001"
  }
}
```

### Environment Variables for Azure App Service

When deploying to Azure App Service, configure these as Application Settings:

- `Jwt__SecretKey`
- `Jwt__Issuer`
- `Jwt__Audience`
- `CosmosDb__ConnectionString`
- `CosmosDb__DatabaseName`
- `AzureCommunicationServices__ConnectionString`
- `AzureCommunicationServices__SenderAddress`
- `BlazorClient__Url`

## Running Locally

### Prerequisites
- .NET 8.0 SDK
- Azurite for blob storage (`npx azurite`)

No Cosmos emulator: local development uses the EF InMemory provider by default
(`Persistence:Provider`). See [docs/local-setup.md](../docs/local-setup.md).

### Steps

1. **Configure settings**:
   - Update `appsettings.Development.json` with your local Cosmos DB connection
   - Set a secure JWT secret key

2. **Run the API**:
   ```bash
   cd MiFuturoTrabajo.Api
   dotnet run
   ```

3. **Access Swagger UI**:
   - Navigate to `https://localhost:5001/swagger` (or the port shown in console)

4. **Test endpoints**:
   - Use Swagger UI or tools like Postman
   - Start with `/api/auth/register` to create a user
   - Then `/api/auth/login` to get an access token
   - Use the Bearer token for authenticated endpoints

## Deployment to Azure App Service

### Option 1: Azure Portal

1. Create an Azure App Service (Free or Basic tier)
2. Configure Application Settings (see above)
3. Deploy using:
   - Visual Studio publish
   - Azure CLI: `az webapp up`
   - GitHub Actions
   - Azure DevOps

### Option 2: Azure CLI

```bash
# Create resource group
az group create --name rg-mifuturotrabajo --location eastus

# Create App Service Plan (Free tier)
az appservice plan create --name plan-mifuturotrabajo --resource-group rg-mifuturotrabajo --sku F1

# Create Web App
az webapp create --name mifuturotrabajo-api --resource-group rg-mifuturotrabajo --plan plan-mifuturotrabajo

# Configure settings
az webapp config appsettings set --name mifuturotrabajo-api --resource-group rg-mifuturotrabajo --settings \
  Jwt__SecretKey="YourProductionSecretKey" \
  CosmosDb__ConnectionString="YourCosmosConnectionString" \
  CosmosDb__DatabaseName="MiFuturoTrabajo"

# Deploy
dotnet publish -c Release
az webapp deploy --name mifuturotrabajo-api --resource-group rg-mifuturotrabajo --src-path ./bin/Release/net8.0/publish.zip
```

## CORS Configuration

The API is configured to allow requests from the Blazor client. Update the `BlazorClient:Url` setting to match your frontend URL.

## Security Features

- JWT-based authentication
- BCrypt password hashing (work factor: 12)
- Account lockout after 5 failed login attempts (30-minute lockout)
- Refresh token rotation
- User isolation (all queries filtered by UserId)

## Differences from Azure Functions

### What Changed
1. **HttpTrigger → Controller Actions**: Azure Functions converted to standard MVC controller actions
2. **HttpRequestData → ControllerBase**: Using ASP.NET Core's built-in request/response handling
3. **Anonymous AuthorizationLevel → [Authorize] Attribute**: Cleaner authorization model
4. **Timer Functions**: moved to their own app, `MiFuturoTrabajo.Functions`, on a
   Consumption plan. App Service Free has no Always On, so a polling
   `BackgroundService` inside this API stops firing when the site unloads -
   silently. See [ADR 0003](../docs/decisions/0003-reminders-as-functions.md).

### What Stayed the Same
1. All business logic and data access
2. Entity models and DTOs
3. JWT authentication implementation
4. Cosmos DB integration via EF Core
5. Same API routes and endpoints (for compatibility with existing clients)

## Future Enhancements

- [x] ~~Create remaining controllers (Meeting, ApplicantTask, Applicant)~~ **COMPLETED**
- [ ] Add Background Services for email reminders (Timer Functions replacement)
- [ ] Add API versioning
- [ ] Add rate limiting
- [ ] Add comprehensive logging with Application Insights
- [ ] Add health checks endpoint
- [ ] Add OpenAPI documentation enhancements
- [ ] Implement repository pattern (optional)
- [ ] Add unit and integration tests

## Notes

- The API maintains backward compatibility with the Azure Functions endpoints
- All custom routes (like `CompanyGetById`, `RecruiterAdd`) are preserved for client compatibility
- Email reminders run as timer triggers in `MiFuturoTrabajo.Functions`, not here
- No dependencies on Azure Functions runtime - pure ASP.NET Core

## Support

See the [main README](../README.md), [architecture notes](../docs/architecture.md)
and the [ADRs](../docs/decisions/).
