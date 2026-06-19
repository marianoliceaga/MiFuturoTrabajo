# MiFuturoTrabajo - Job Application Tracker

A comprehensive job application tracking system built with Blazor WebAssembly and ASP.NET Core Web API, featuring custom JWT authentication with OAuth support (Google, Microsoft) and Azure Cosmos DB storage.

## Project Structure

```
MiFuturoTrabajo/
├── MiFuturoTrabajo.Client/              # Blazor WebAssembly Frontend
├── MiFuturoTrabajo.Api/                 # ASP.NET Core Web API Backend
├── MiFuturoTrabajo.Infrastructure/      # EF Core & Data Access Layer
├── MiFuturoTrabajo.Shared/              # DTOs, Models & Enums
├── MiFuturoTrabajo.Tests.Unit/          # Unit Tests (xUnit)
├── MiFuturoTrabajo.Tests.Integration/   # Integration Tests (xUnit)
└── README.md                        # This file
```

## Features

### Frontend (Blazor WebAssembly)
- ✅ Job Application Management (Kanban Board & List View with drag-and-drop)
- ✅ Company Management
- ✅ Recruiter Contacts
- ✅ Interview Scheduling (Meetings)
- ✅ Task Management
- ✅ Profile & CV Management (Multiple CV versions with upload)
- ✅ Job References Management
- ✅ Authentication (Email/Password + Google OAuth + Microsoft OAuth)
- ✅ User Logout with Token Revocation

### Backend (ASP.NET Core Web API)
- ✅ Complete CRUD APIs for all entities (8 controllers)
- ✅ Custom JWT Token Authentication
- ✅ OAuth Integration (Google & Microsoft)
- ✅ Password Reset & Recovery
- ✅ Email Verification (skeleton implemented)
- ✅ Account Lockout (5 failed attempts)
- ✅ Token Refresh Mechanism
- ✅ User Data Isolation (multi-tenant ready)
- ✅ EF Core with Cosmos DB
- ✅ Swagger/OpenAPI Documentation

### Testing
- ✅ Comprehensive Unit Tests
  - Service layer tests (JWT, Password Hashing, Mapping)
  - Controller tests (AuthController with mocked dependencies)
- ✅ Comprehensive Integration Tests
  - End-to-end authentication flow tests
  - Full CRUD operations for all entities
  - Kanban board functionality tests
  - WebApplicationFactory-based testing

### Security
- ✅ Custom JWT Authentication
- ✅ BCrypt Password Hashing
- ✅ OAuth 2.0 / OpenID Connect (Google, Microsoft)
- ✅ JWT Token Validation & Refresh
- ✅ Token Revocation on Logout
- ✅ Account Lockout Protection
- ✅ User Data Isolation

## Technology Stack

- **.NET 8**
- **Blazor WebAssembly**
- **ASP.NET Core Web API** (NOT Azure Functions)
- **Entity Framework Core**
- **Azure Cosmos DB** (or Cosmos DB Emulator for local development)
- **Custom JWT Authentication** (NOT Microsoft Entra)
- **Google.Apis.Auth** (Google OAuth)
- **Microsoft.Identity.Client** (Microsoft OAuth)
- **BCrypt.Net-Next** (Password Hashing)
- **xUnit** (Testing Framework)
- **Moq** (Mocking Framework)

## Database Entities

The application manages 12 entities:

1. **User** - Authentication and user accounts
2. **Applicant** - User profile information
3. **Company** - Companies where jobs are applied
4. **Recruiter** - Recruiter contacts
5. **JobApplication** - Job applications with status tracking
6. **Meeting** - Interviews and meetings
7. **ApplicantTask** - Tasks and follow-ups
8. **CVVersion** - Multiple CV versions per applicant
9. **JobReference** - Past job references
10. **InterviewStep** - Interview process stages
11. **JobApplicationRecruiter** - Many-to-many relationship
12. **MeetingRecruiter** - Many-to-many relationship

## Getting Started

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022 or VS Code
- Azure Cosmos DB Emulator (for local development) OR Azure Cosmos DB account
- Node.js (for Blazor dev server)

### Configuration

#### 1. API Backend

Update `MiFuturoTrabajo.Api/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "CosmosDb": {
    "ConnectionString": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    "DatabaseName": "MiFuturoTrabajo"
  },
  "Jwt": {
    "SecretKey": "YourVeryLongSecretKeyHere-MinimumOf32Characters!",
    "Issuer": "MiFuturoTrabajo",
    "Audience": "MiFuturoTrabajo.Client",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 7
  },
  "OAuth": {
    "Google": {
      "ClientId": "your-google-client-id.apps.googleusercontent.com"
    },
    "Microsoft": {
      "ClientId": "your-microsoft-client-id"
    }
  },
  "AzureCommunicationServices": {
    "ConnectionString": "endpoint=https://your-acs.communication.azure.com/;accesskey=your-key",
    "SenderAddress": "DoNotReply@your-domain.com"
  },
  "AllowedHosts": "*"
}
```

#### 2. Blazor Client

Update `MiFuturoTrabajo.Client/wwwroot/appsettings.json`:

```json
{
  "ApiBaseUrl": "https://localhost:5011/api/"
}
```

### Running Locally

#### 1. Start Cosmos DB Emulator
Download and run the [Azure Cosmos DB Emulator](https://aka.ms/cosmosdb-emulator)

#### 2. Start API Backend
```bash
cd MiFuturoTrabajo.Api
dotnet run
```

The API will be available at `https://localhost:5011`
Swagger UI: `https://localhost:5011/swagger`

#### 3. Start Blazor Client
```bash
cd MiFuturoTrabajo.Client
dotnet run
```

The app will be available at `https://localhost:5001`

### Running Tests

#### Unit Tests
```bash
cd MiFuturoTrabajo.Tests.Unit
dotnet test
```

#### Integration Tests
```bash
cd MiFuturoTrabajo.Tests.Integration
dotnet test
```

#### All Tests
```bash
dotnet test
```

### Building for Production

```bash
dotnet build -c Release
```

## API Endpoints

All endpoints require JWT Bearer token authentication (except auth endpoints).

### Authentication (`/api/auth`)
- `POST /api/auth/register` - Register new user (Email/Password)
- `POST /api/auth/login` - Login (Email/Password)
- `POST /api/auth/logout` - Logout and revoke refresh token (NEW!)
- `POST /api/auth/oauth-login` - OAuth login (Google/Microsoft)
- `POST /api/auth/refresh` - Refresh access token
- `GET /api/auth/me` - Get current user profile
- `POST /api/auth/change-password` - Change password
- `POST /api/auth/forgot-password` - Request password reset
- `POST /api/auth/reset-password` - Reset password with token
- `POST /api/auth/send-verification` - Send email verification
- `POST /api/auth/verify-email` - Verify email with token

### Companies (`/api/companies`)
- `GET /api/companies` - Get all companies
- `GET /api/companies/{id}` - Get company by ID
- `POST /api/companies` - Create company
- `PUT /api/companies/{id}` - Update company
- `DELETE /api/companies/{id}` - Delete company

### Recruiters (`/api/recruiters`)
- `GET /api/recruiters` - Get all recruiters
- `GET /api/recruiters/{id}` - Get recruiter by ID
- `POST /api/recruiters` - Create recruiter
- `PUT /api/recruiters/{id}` - Update recruiter
- `DELETE /api/recruiters/{id}` - Delete recruiter

### Job Applications (`/api/jobapplications`)
- `GET /api/jobapplications` - Get all job applications
- `GET /api/jobapplications/{id}` - Get application by ID
- `GET /api/jobapplications/board` - Get Kanban board view
- `POST /api/jobapplications` - Create application
- `PUT /api/jobapplications/{id}` - Update application
- `PUT /api/jobapplications/{id}/status` - Update application status (for Kanban)
- `DELETE /api/jobapplications/{id}` - Delete application

### Meetings (`/api/meetings`)
- `GET /api/meetings` - Get all meetings
- `GET /api/meetings/{id}` - Get meeting by ID
- `POST /api/meetings` - Create meeting
- `PUT /api/meetings/{id}` - Update meeting
- `DELETE /api/meetings/{id}` - Delete meeting

### Applicant Tasks (`/api/applicanttasks`)
- `GET /api/applicanttasks` - Get all tasks
- `GET /api/applicanttasks/{id}` - Get task by ID
- `POST /api/applicanttasks` - Create task
- `PUT /api/applicanttasks/{id}` - Update task
- `DELETE /api/applicanttasks/{id}` - Delete task

### CV Versions (`/api/cvversions`)
- `GET /api/cvversions` - Get all CV versions
- `GET /api/cvversions/{id}` - Get CV version by ID
- `POST /api/cvversions` - Create CV version
- `PUT /api/cvversions/{id}` - Update CV version
- `PUT /api/cvversions/{id}/default` - Set as default CV
- `DELETE /api/cvversions/{id}` - Delete CV version

### Applicant (`/api/applicant`)
- `GET /api/applicant` - Get applicant profile
- `POST /api/applicant` - Create/Update applicant profile
- `POST /api/applicant/cv` - Upload CV (Note: Currently accepts URL string)
- `POST /api/applicant/reference` - Add job reference
- `DELETE /api/applicant/reference/{id}` - Delete job reference

## Job Application Statuses (Kanban Columns)

1. **NotApplied** - Job saved but not yet applied
2. **Applied** - Application submitted
3. **FirstInterview** - First round interview
4. **Meetings** - Additional interview rounds
5. **Rejected** - Application rejected
6. **Offer** - Job offer received
7. **Completed** - Process completed (accepted/declined)
8. **NoResponse** - No response from company

## Database Schema (Cosmos DB)

The application uses Azure Cosmos DB with the following containers:

- **Users** (Partition Key: id) - Authentication
- **Applicants** (Partition Key: UserId)
- **Companies** (Partition Key: UserId)
- **Recruiters** (Partition Key: UserId)
- **JobApplications** (Partition Key: ApplicantId)
- **Meetings** (Partition Key: ApplicantId)
- **ApplicantTasks** (Partition Key: ApplicantId)
- **CVVersions** (Partition Key: ApplicantId)
- **JobReferences** (Partition Key: ApplicantId)
- **InterviewSteps** (Partition Key: JobApplicationId)
- **JobApplicationRecruiters** (Partition Key: JobApplicationId)
- **MeetingRecruiters** (Partition Key: MeetingId)

## OAuth Setup

For detailed OAuth setup instructions, see [OAUTH_SETUP.md](OAUTH_SETUP.md)

### Quick OAuth Setup

1. **Google OAuth:**
   - Create project at [Google Cloud Console](https://console.cloud.google.com)
   - Enable Google+ API
   - Create OAuth 2.0 Client ID
   - Add authorized redirect URIs
   - Copy Client ID to appsettings.json

2. **Microsoft OAuth:**
   - Register app at [Azure Portal](https://portal.azure.com)
   - Add redirect URIs
   - Copy Application (client) ID to appsettings.json

## Deployment

### API Backend
Deploy to Azure App Service:
```bash
cd MiFuturoTrabajo.Api
dotnet publish -c Release
```

### Blazor WebAssembly
Deploy to Azure Static Web Apps or any static hosting:
```bash
cd MiFuturoTrabajo.Client
dotnet publish -c Release
```
Deploy the `bin/Release/net8.0/publish/wwwroot` folder.

## Development Notes

### Recent Updates (Current Version)
- ✅ Added logout endpoint with token revocation
- ✅ Created comprehensive unit test suite (3 test files, 50+ tests)
- ✅ Created comprehensive integration test suite (3 test files, 30+ tests)
- ✅ Added WebApplicationFactory for integration testing
- ✅ All CRUD operations fully tested
- ✅ Authentication flows fully tested
- ✅ Kanban board functionality fully tested

### Architecture Highlights
- **Clean Architecture**: Separation of concerns across layers
- **Direct EF Core usage**: No repository pattern for simplicity
- **JWT-based authentication**: Custom implementation with refresh tokens
- **OAuth Integration**: Google and Microsoft sign-in
- **User Isolation**: All data scoped to authenticated user
- **Cosmos DB NoSQL**: Cloud-native scalability

### Known Limitations
- Email reminder services exist but are currently disabled (commented out in Program.cs)
- CV file upload accepts URL strings only (no actual file storage implementation yet)
- Email verification implemented as skeleton (emails not actually sent)

## Testing Coverage

### Unit Tests
- **PasswordHasherTests**: BCrypt hashing and verification (7 tests)
- **JwtServiceTests**: Token generation and validation (14 tests)
- **MappingServiceTests**: DTO-Entity mappings (15 tests)
- **AuthControllerTests**: Authentication endpoints (12 tests)

### Integration Tests
- **AuthenticationFlowTests**: Complete auth flows (12 tests)
- **JobApplicationCrudTests**: All entity CRUD operations (8 tests)
- **KanbanBoardTests**: Board view and status updates (12 tests)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Write tests for new features
4. Ensure all tests pass: `dotnet test`
5. Commit your changes
6. Push to the branch
7. Create a Pull Request

## License

This project is licensed under the MIT License - see [LICENSE.txt](LICENSE.txt)

## Changelog

### Current Version
- ASP.NET Core Web API (NOT Azure Functions)
- Custom JWT Authentication (NOT Microsoft Entra)
- OAuth support (Google, Microsoft)
- Comprehensive test suite
- Token revocation on logout
- Kanban board with drag-and-drop
- Complete CRUD for all entities

---

**Built with ❤️ using Blazor WebAssembly and ASP.NET Core**
