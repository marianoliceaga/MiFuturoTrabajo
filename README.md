# Letmework4U - Job Application Tracker

A comprehensive job application tracking system built with Blazor WebAssembly and Azure Functions, featuring Microsoft Entra External ID authentication and Azure Cosmos DB storage.

## Project Structure

```
Letmework4U/
├── Letmework4U.Client/              # Blazor WebAssembly Frontend
├── Letmework4U.Functions/           # Azure Functions Backend API
├── Letmework4U.Infrastructure/      # EF Core & Data Access
├── Letmework4U.Shared/             # DTOs & Models
├── Letmework4U.Core/               # Business Logic (future use)
├── IMPLEMENTATION_GUIDE.md         # Detailed implementation guide
└── README.md                       # This file
```

## Features

### Frontend (Blazor WebAssembly)
- ✅ Job Application Management (Kanban Board & List View)
- ✅ Company Management
- ✅ Recruiter Contacts
- ✅ Interview Scheduling
- ✅ Task Management
- ✅ Profile & CV Management
- ✅ Microsoft Entra External ID Authentication (MSAL)

### Backend (Azure Functions)
- ✅ Complete CRUD APIs for all entities
- ✅ JWT Token Authentication
- ✅ User Isolation (multi-tenant ready)
- ✅ Email Reminders (Interviews & Tasks)
- ✅ EF Core with Cosmos DB

### Security
- ✅ Microsoft Entra External ID Integration
- ✅ OAuth 2.0 / OpenID Connect
- ✅ JWT Token Validation
- ✅ Automatic Token Refresh
- ✅ User Data Isolation

## Technology Stack

- **.NET 8**
- **Blazor WebAssembly**
- **Azure Functions (Isolated Worker Model)**
- **Entity Framework Core**
- **Azure Cosmos DB**
- **Microsoft Entra External ID**
- **Azure Communication Services** (Email)
- **Microsoft Authentication Library (MSAL)**

## Getting Started

### Prerequisites

- .NET 8 SDK
- Azure Subscription
- Visual Studio 2022 or VS Code
- Azure Functions Core Tools
- Node.js (for Blazor dev server)

### Azure Resources Required

1. **Microsoft Entra External ID Tenant** - Authentication
2. **Azure Cosmos DB** - Database
3. **Azure Communication Services** - Email notifications
4. **Azure Blob Storage** - CV file storage (optional)

### Configuration

#### 1. Azure Functions (Backend)
 
Update `Letmework4U.Functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDb__ConnectionString": "<your-cosmos-connection-string>",
    "CosmosDb__DatabaseName": "Letmework4U",
    "MicrosoftEntraExternalId__Instance": "https://<your-tenant>.ciamlogin.com",
    "MicrosoftEntraExternalId__Domain": "<your-tenant>.onmicrosoft.com",
    "MicrosoftEntraExternalId__ClientId": "<your-api-client-id>",
    "MicrosoftEntraExternalId__SignUpSignInPolicyId": "susi",
    "AzureCommunicationServices__ConnectionString": "<your-acs-connection-string>",
    "AzureCommunicationServices__SenderAddress": "DoNotReply@<your-domain>.com"
  }
}
```

#### 2. Blazor Client (Frontend)

Update `Letmework4U.Client/wwwroot/appsettings.json`:

```json
{
  "MicrosoftEntraExternalId": {
    "Authority": "https://<your-tenant>.ciamlogin.com/<your-tenant>.onmicrosoft.com/susi",
    "ClientId": "<your-blazor-client-id>",
    "ValidateAuthority": false
  },
  "ApiBaseUrl": "http://localhost:7071/api/"
}
```

### Running Locally

#### 1. Start Azure Functions
```bash
cd Letmework4U.Functions
func start
```

The API will be available at `http://localhost:7071`

#### 2. Start Blazor Client
```bash
cd Letmework4U.Client
dotnet run
```

The app will be available at `https://localhost:5001`

### Building for Production

```bash
dotnet build -c Release
```

## API Endpoints

### Authentication
All endpoints require JWT authentication via Microsoft Entra External ID (except health checks).

### Companies
- `GET /api/Companies` - Get all companies
- `GET /api/Companies/CompanyGetById/{id}` - Get company by ID
- `POST /api/Companies/CompanyAdd` - Create company
- `PATCH /api/Companies/{id}` - Update company
- `DELETE /api/Companies/{id}` - Delete company

### Recruiters
- `GET /api/Recruiters` - Get all recruiters
- `GET /api/Recruiters/RecruiterGetById/{id}` - Get recruiter by ID
- `POST /api/Recruiters/RecruiterAdd` - Create recruiter
- `PATCH /api/Recruiters/{id}` - Update recruiter
- `DELETE /api/Recruiters/RecruiterDeleteById/{id}` - Delete recruiter

### Job Applications
- `GET /api/JobApplications` - Get all job applications
- `GET /api/JobApplications/JobApplicationGetById/{id}` - Get application by ID
- `GET /api/JobApplications/board` - Get board view (grouped by status)
- `POST /api/JobApplications/JobApplicationAdd` - Create application
- `PATCH /api/JobApplications/{id}` - Update application
- `PATCH /api/JobApplications/{id}/status` - Update application status
- `DELETE /api/JobApplications/JobApplicationDeleteById/{id}` - Delete application

### Meetings
- `GET /api/Meetings` - Get all meetings
- `GET /api/Meetings/MeetingGetById/{id}` - Get meeting by ID
- `POST /api/Meetings/MeetingAdd` - Create meeting
- `PATCH /api/Meetings/{id}` - Update meeting
- `DELETE /api/Meetings/MeetingDeleteById/{id}` - Delete meeting

### Applicant Tasks
- `GET /api/ApplicantTasks` - Get all tasks
- `GET /api/ApplicantTasks/ApplicantTaskGetById/{id}` - Get task by ID
- `POST /api/ApplicantTasks/ApplicantTaskAdd` - Create task
- `PATCH /api/ApplicantTasks/{id}` - Update task
- `DELETE /api/ApplicantTasks/ApplicantTaskDeleteById/{id}` - Delete task

### Timer Functions (Automated)
- **Interview Reminder** - Runs every hour, sends email reminders
- **Task Reminder** - Runs daily at 8 AM UTC, sends task due reminders

## Database Schema

The application uses Azure Cosmos DB with the following containers:

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

## Deployment

### Azure Functions
```bash
cd Letmework4U.Functions
func azure functionapp publish <your-function-app-name>
```

### Blazor WebAssembly
Deploy to Azure Static Web Apps or any static hosting:
```bash
cd Letmework4U.Client
dotnet publish -c Release
```

Then deploy the `bin/Release/net8.0/publish/wwwroot` folder.

## Development Notes

### Architecture Changes
This version has been completely refactored from the original:
- ❌ Removed Repository Pattern
- ✅ Direct EF Core usage in Azure Functions
- ✅ JWT-based authentication replaces custom auth
- ✅ Microsoft Entra External ID for enterprise-grade security
- ✅ Cosmos DB for cloud-native scalability

### Migration from Old Code
The original `Letmework4U.Client/Services/` implementations are replaced by:
- Direct HTTP calls to Azure Functions
- MSAL-managed authentication
- Automatic token attachment to requests

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## License

This project is licensed under the MIT License.

## Support

For detailed implementation instructions, see [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md)

## Changelog

### Version 2.0 (Current)
- Complete rewrite to .NET 8
- Azure Functions backend
- Microsoft Entra External ID authentication
- EF Core with Cosmos DB
- Automated email reminders
- Multi-tenant architecture

### Version 1.0 (Legacy)
- Blazor WebAssembly with repository pattern
- In-memory authentication
- No backend API

---

**Built with ❤️ using Blazor and Azure**
