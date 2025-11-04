using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Letmework4U.Infrastructure;
using Letmework4U.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Custom JWT Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Letmework4U";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Letmework4U.Client";

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

// Configure Entity Framework Core with Cosmos DB
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
    var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"];

    if (!string.IsNullOrEmpty(cosmosConnectionString) && !string.IsNullOrEmpty(cosmosDatabaseName))
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
var blazorClientUrl = builder.Configuration["BlazorClient:Url"] ?? "https://localhost:5001";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(blazorClientUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add controllers
builder.Services.AddControllers()
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

// Enable CORS
app.UseCors("AllowBlazorClient");

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
