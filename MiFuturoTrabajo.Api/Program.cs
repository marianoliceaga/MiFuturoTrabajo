using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Client.Services;

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

// Configure Entity Framework Core with Cosmos DB or InMemory
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
    var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"];

    if (!string.IsNullOrEmpty(cosmosConnectionString) && cosmosConnectionString.Contains("localhost:8081"))
    {
        options.UseInMemoryDatabase("MiFuturoTrabajoLocal");
    }
    else if (!string.IsNullOrEmpty(cosmosConnectionString) && !string.IsNullOrEmpty(cosmosDatabaseName))
    {
        options.UseCosmos(cosmosConnectionString, cosmosDatabaseName);
    }
});

// Register Authentication Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IOAuthService, OAuthService>();

// Register Email Service
builder.Services.AddSingleton<IEmailService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<EmailService>>();
    var connectionString = config["AzureCommunicationServices:ConnectionString"] ?? "dummy";
    var senderAddress = config["AzureCommunicationServices:SenderAddress"] ?? "test@example.com";

    return new EmailService(connectionString, senderAddress, logger);
});

// Register Background Services for Timer Functions
// Uncomment when you want to enable email reminders
// builder.Services.AddHostedService<TaskReminderHostedService>();
// builder.Services.AddHostedService<InterviewReminderHostedService>();

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
builder.Services.AddControllers()
    .AddNewtonsoftJson(); // For Newtonsoft.Json serialization

// Add API Explorer for Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "https://localhost:5011";
builder.Services.AddHttpClient("MiFuturoTrabajo.Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
}).AddHttpMessageHandler<AuthenticationHttpMessageHandler>();

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
