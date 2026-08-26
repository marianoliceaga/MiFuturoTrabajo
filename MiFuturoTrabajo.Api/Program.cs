using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Custom JWT Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MiFuturoTrabajo";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MiFuturoTrabajo.Client";

if (string.IsNullOrEmpty(jwtSecretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured in appsettings.json");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Persistence provider is an explicit choice, not a side effect of what the
// connection string happens to contain. The old code silently swapped in the
// InMemory provider whenever the string mentioned localhost:8081, which meant
// "running on the Cosmos emulator" actually meant "running on a volatile
// in-memory store" - and hid the fact that the data access could not run on
// Cosmos at all.
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Cosmos";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase("MiFuturoTrabajoLocal");
        return;
    }

    var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
    var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"];

    if (string.IsNullOrEmpty(cosmosConnectionString) || string.IsNullOrEmpty(cosmosDatabaseName))
    {
        throw new InvalidOperationException(
            "Persistence:Provider is 'Cosmos' but CosmosDb:ConnectionString / CosmosDb:DatabaseName " +
            "are not configured. Set them, or set Persistence:Provider to 'InMemory' for a " +
            "throwaway local store.");
    }

    options.UseCosmos(cosmosConnectionString, cosmosDatabaseName);
});

// Register application services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IOAuthService, OAuthService>();
// One BlobServiceClient for the process: it is thread-safe, holds the
// connection pool, and re-creating it per request throws that pool away.
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ICvDocumentService, CvDocumentService>();

// Email. Degrades to a no-op locally, but NOT in Production: an app that
// silently stops sending password-reset mail is an app whose users cannot get
// back into their accounts, and the failure is invisible until someone
// complains. Better to refuse to start.
var emailConnectionString = builder.Configuration["AzureCommunicationServices:ConnectionString"];
var emailSenderAddress = builder.Configuration["AzureCommunicationServices:SenderAddress"];

var emailIsConfigured =
    !string.IsNullOrWhiteSpace(emailConnectionString)
    && !emailConnectionString.Contains("YOUR_ACS")
    && !string.IsNullOrWhiteSpace(emailSenderAddress)
    && !emailSenderAddress.Contains("your-domain");

if (!emailIsConfigured && builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "AzureCommunicationServices:ConnectionString and :SenderAddress must be set in Production. " +
        "Email verification and password reset do not work without them.");
}

builder.Services.AddSingleton<IEmailService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<EmailService>>();

    if (emailIsConfigured)
    {
        return new EmailService(emailConnectionString!, emailSenderAddress!, logger);
    }

    logger.LogWarning(
        "No email provider configured. Verification and password-reset links will be written " +
        "to this log instead of being sent.");

    return new NoOpEmailService(logger);
});

// Register background services
builder.Services.AddHostedService<TaskReminderHostedService>();
builder.Services.AddHostedService<InterviewReminderHostedService>();

// Configure CORS for Blazor Client
var blazorClientUrl = builder.Configuration["BlazorClient:Url"] ?? "https://localhost:5011";
builder.Services.AddCors(options =>
{
    // Production policy - only allow specific origins
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5001",
                "https://localhost:5011",
                blazorClientUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    // Development policy - allow any origin (for Postman, Swagger, etc.)
    options.AddPolicy("AllowDevelopment", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add controllers
builder.Services.AddControllers(options =>
{
    // DTOs describe the wire shape, not a required-field contract. Without this,
    // every non-nullable string property is implicitly [Required] and optional
    // fields (Address, Telephone, ...) reject the request with a 400.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
    .AddNewtonsoftJson(); // For Newtonsoft.Json serialization

// Add API Explorer for Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Enable CORS - use permissive policy in Development, restrictive in Production
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowDevelopment");
}
else
{
    app.UseCors("AllowBlazorClient");
}

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ensure Cosmos DB database is created (Development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // This creates the database if it doesn't exist
        await dbContext.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("Cosmos DB database and containers ensured to exist.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error ensuring Cosmos DB database exists. Make sure the Cosmos DB Emulator is running.");
    }
}

app.Run();

// Make the Program class public for integration tests
public partial class Program { }
