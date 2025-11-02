# Proprietary Authentication System - Migration Guide

This guide documents the migration from Microsoft Entra External ID (MSAL) to a proprietary JWT-based authentication system.

## Overview

We are replacing the external authentication provider (Microsoft Entra External ID) with a custom authentication system that includes:

- Custom user management with email/password authentication
- JWT token-based authentication
- Password hashing using BCrypt
- Refresh token support
- Account security features (lockout, failed attempts tracking)

---

## ✅ Completed Steps

### 1. Database & Entities

**Created Files:**
- `Letmework4U.Infrastructure/Entities/User.cs` - User entity for authentication
- `Letmework4U.Shared/Models/AuthDTOs.cs` - Authentication request/response DTOs

**Updated Files:**
- `Letmework4U.Infrastructure/ApplicationDbContext.cs` - Added Users DbSet and configuration
- `Letmework4U.Infrastructure/Entities/Applicant.cs` - Updated UserId comment to reference proprietary auth

**User Entity Features:**
- Email-based authentication
- Password hashing storage
- Email confirmation support
- Password reset tokens
- Refresh tokens for JWT
- Account lockout after failed attempts
- One-to-one relationship with Applicant

### 2. Backend Services

**Created Files:**
- `Letmework4U.Functions/Services/JwtService.cs` - JWT token generation and validation
- `Letmework4U.Functions/Services/PasswordHasher.cs` - BCrypt password hashing
- `Letmework4U.Functions/Functions/AuthFunctions.cs` - Authentication endpoints

**Installed Packages:**
- `System.IdentityModel.Tokens.Jwt` (v8.14.0) - JWT handling
- `BCrypt.Net-Next` (v4.0.3) - Password hashing

**Authentication Endpoints:**
- `POST /api/auth/register` - User registration
- `POST /api/auth/login` - User login
- `POST /api/auth/refresh` - Refresh access token
- `GET /api/auth/me` - Get current user profile

**Security Features Implemented:**
- Password hashing with BCrypt (work factor: 12)
- Account lockout after 5 failed login attempts (30-minute lockout)
- Refresh token rotation
- Token expiration (access: 60 min, refresh: 7-30 days)
- Email validation
- Password strength validation (minimum 8 characters)

---

## 📋 Remaining Tasks

### 3. Backend Configuration

**Need to Update:**
- `Letmework4U.Functions/Program.cs`
  - Remove Microsoft Identity Web configuration
  - Register JwtService and PasswordHasher
  - Add custom JWT authentication middleware
  - Configure JWT settings in local.settings.json

**Need to Add to `local.settings.json`:**
```json
{
  "Values": {
    "Jwt:SecretKey": "YOUR_SUPER_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG_FOR_SECURITY",
    "Jwt:Issuer": "Letmework4U",
    "Jwt:Audience": "Letmework4U.Client",
    "Jwt:AccessTokenExpiryMinutes": "60",
    "Jwt:RefreshTokenExpiryDays": "7"
  }
}
```

### 4. Update Existing Azure Functions

**Files to Update:**
All existing Function files need to be updated to use custom JWT authentication instead of Microsoft Identity:

- `CompanyFunctions.cs`
- `RecruiterFunctions.cs`
- `ApplicantTaskFunctions.cs`
- `MeetingFunctions.cs`
- `JobApplicationFunctions.cs`
- `ApplicantFunctions.cs`

**Changes Needed:**
1. Remove Microsoft Identity Web dependencies
2. Extract user ID from custom JWT token
3. Use `IJwtService` to validate tokens
4. Update authorization logic

**Example Pattern:**
```csharp
// OLD (MSAL):
var userId = req.HttpContext.User.FindFirst("sub")?.Value
             ?? req.HttpContext.User.FindFirst("oid")?.Value;

// NEW (Custom JWT):
private string? GetUserIdFromRequest(HttpRequestData req, IJwtService jwtService)
{
    var token = req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "");
    if (string.IsNullOrEmpty(token)) return null;
    return jwtService.GetUserIdFromToken(token);
}
```

### 5. Frontend - Remove MSAL

**Need to Update:**
- `Letmework4U.Client/Program.cs`
  - Remove MSAL authentication configuration
  - Remove AuthorizationMessageHandler
  - Set up simple HttpClient configuration

**Need to Remove Packages:**
```bash
cd Letmework4U.Client
dotnet remove package Microsoft.Authentication.WebAssembly.Msal
```

**Need to Update:**
- `Letmework4U.Client/wwwroot/index.html` - Remove MSAL script references
- `Letmework4U.Client/App.razor` - Remove MSAL authentication components

### 6. Frontend - Custom Authentication

**Need to Create:**

**Authentication State Provider:**
- `Letmework4U.Client/Services/CustomAuthStateProvider.cs`
  - Manage authentication state
  - Store tokens in localStorage/sessionStorage
  - Provide user claims
  - Handle token expiration

**Authentication Service:**
- `Letmework4U.Client/Services/IAuthenticationService.cs` (interface)
- `Letmework4U.Client/Services/AuthenticationService.cs` (implementation)
  - Login(email, password)
  - Register(registerRequest)
  - Logout()
  - RefreshToken()
  - GetCurrentUser()

**HTTP Interceptor:**
- `Letmework4U.Client/Services/AuthenticationHttpMessageHandler.cs`
  - Automatically attach JWT token to requests
  - Handle 401 responses (refresh token or redirect to login)
  - Handle token expiration

**UI Components:**
- `Letmework4U.Client/Pages/Login.razor` - Login page
- `Letmework4U.Client/Pages/Register.razor` - Registration page
- `Letmework4U.Client/Pages/Logout.razor` - Logout handler
- `Letmework4U.Client/Shared/LoginDisplay.razor` - Update to use custom auth

### 7. Update All Client Services

**Files to Update:**
All services currently using `AccessTokenNotAvailableException` need to be updated:

- `ApplicantService.cs`
- `CompanyService.cs`
- `RecruiterService.cs`
- `JobApplicationService.cs`
- `MeetingService.cs`
- `ApplicantTaskService.cs`

**Changes Needed:**
1. Remove `AccessTokenNotAvailableException` handling
2. Handle HTTP 401 responses instead
3. Tokens will be attached automatically by `AuthenticationHttpMessageHandler`

### 8. Testing & Validation

**Need to Test:**
- [ ] User registration flow
- [ ] User login flow
- [ ] Token refresh flow
- [ ] Failed login attempts and account lockout
- [ ] Logout and token cleanup
- [ ] All CRUD operations with JWT authentication
- [ ] Token expiration handling
- [ ] Concurrent requests with token refresh
- [ ] Browser refresh maintaining auth state

---

## Migration Steps (Recommended Order)

### Step 1: Backend Configuration (30 minutes)
1. Update `Program.cs` in Functions project
2. Add JWT configuration to `local.settings.json`
3. Test authentication endpoints with Postman/curl

### Step 2: Update Existing Functions (1-2 hours)
1. Create a helper method for JWT token extraction
2. Update each Function file to use custom JWT
3. Remove Microsoft Identity Web references
4. Test each endpoint individually

### Step 3: Frontend Authentication Setup (2-3 hours)
1. Remove MSAL packages and configuration
2. Create `CustomAuthStateProvider`
3. Create `AuthenticationService`
4. Create `AuthenticationHttpMessageHandler`
5. Register services in `Program.cs`

### Step 4: Create Login/Register UI (1-2 hours)
1. Create `Login.razor` page
2. Create `Register.razor` page
3. Update `LoginDisplay.razor`
4. Update `App.razor` routing

### Step 5: Update Client Services (1 hour)
1. Remove `AccessTokenNotAvailableException` handling from all services
2. Test API calls with new authentication

### Step 6: End-to-End Testing (1-2 hours)
1. Test complete registration → login → use app flow
2. Test token refresh
3. Test logout
4. Test concurrent sessions
5. Test error scenarios

**Total Estimated Time: 6-10 hours**

---

## Configuration Reference

### JWT Settings

**Secret Key Generation:**
```bash
# Generate a secure random key (PowerShell)
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | ForEach-Object {[char]$_})

# Or use online tool: https://generate-random.org/api-key-generator
```

**Recommended Settings:**
- Access Token Expiry: 60 minutes (short for security)
- Refresh Token Expiry: 7 days (30 days if "Remember Me")
- Password Work Factor: 12 (BCrypt)
- Account Lockout: 30 minutes after 5 failed attempts

### Security Considerations

1. **HTTPS Only:** Always use HTTPS in production
2. **Secure Storage:** Store tokens in httpOnly cookies or secure localStorage
3. **CORS:** Configure CORS properly in Azure Functions
4. **Token Rotation:** Rotate refresh tokens on each use
5. **Password Policy:** Enforce strong passwords (min 8 chars, complexity)
6. **Rate Limiting:** Consider adding rate limiting to auth endpoints
7. **Email Verification:** Implement email verification before allowing login
8. **Password Reset:** Implement secure password reset flow

---

## Rollback Plan

If needed to rollback to MSAL:

1. **Keep old configuration files:**
   - Backup `appsettings.json` with MSAL config
   - Keep Entra ID app registrations

2. **Database:**
   - Users table can coexist with old system
   - Applicant.UserId can store either MSAL sub or custom User.Id

3. **Code:**
   - Git branch before starting migration
   - Tag current commit: `git tag pre-custom-auth`

---

## Next Steps

1. **Decide on migration approach:**
   - Complete migration (remove MSAL entirely)
   - Hybrid approach (support both MSAL and custom auth)

2. **Review security requirements:**
   - Do you need email verification?
   - Do you need password reset?
   - Do you need two-factor authentication (2FA)?
   - Do you need social login (Google, Facebook)?

3. **Plan deployment:**
   - Staged rollout or big bang?
   - Data migration strategy?
   - User communication plan?

---

## Questions to Answer

Before continuing, please clarify:

1. **Do you want to completely remove MSAL**, or keep it as an alternative login method?
2. **Do you need email verification** for new registrations?
3. **Do you need password reset functionality**?
4. **Do you still want social login** (Google, Facebook) in the future?
5. **What is your timeline** for this migration?
6. **Do you want me to continue** implementing the remaining steps, or would you like to review what we have so far?

---

## Files Created So Far

### Infrastructure:
- ✅ `Letmework4U.Infrastructure/Entities/User.cs`
- ✅ `Letmework4U.Shared/Models/AuthDTOs.cs`
- ✅ Updated: `Letmework4U.Infrastructure/ApplicationDbContext.cs`

### Functions (Backend):
- ✅ `Letmework4U.Functions/Services/JwtService.cs`
- ✅ `Letmework4U.Functions/Services/PasswordHasher.cs`
- ✅ `Letmework4U.Functions/Functions/AuthFunctions.cs`

### Documentation:
- ✅ This guide: `PROPRIETARY_AUTH_MIGRATION_GUIDE.md`

---

**Ready for next steps when you are!**
