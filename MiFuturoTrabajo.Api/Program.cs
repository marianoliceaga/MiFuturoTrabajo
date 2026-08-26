using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MiFuturoTrabajo.Infrastructure;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure.Email;

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
// Entra's signing keys, fetched once and refreshed on their own schedule
// rather than per sign-in. A short HTTP timeout matters here: if Microsoft is
// unreachable the validator refuses the token, and it should do so promptly
// instead of holding the request open.
builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(_ =>
{
    var tenantId = builder.Configuration["OAuth:Microsoft:TenantId"] ?? "common";

    return new ConfigurationManager<OpenIdConnectConfiguration>(
        $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever(new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            RequireHttps = true
        });
});

builder.Services.AddScoped<IOAuthService, OAuthService>();
// One BlobServiceClient for the process: it is thread-safe, holds the
// connection pool, and re-creating it per request throws that pool away.
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ICvDocumentService, CvDocumentService>();

// Registration is open, so both "how fast" and "how much" need a ceiling.
var rateLimitOptions = new RateLimitOptions();
builder.Configuration.GetSection("RateLimits").Bind(rateLimitOptions);
builder.Services.AddSingleton(rateLimitOptions);
builder.Services.AddAppRateLimiting(rateLimitOptions);

var quotaOptions = new QuotaOptions();
builder.Configuration.GetSection("Quotas").Bind(quotaOptions);
builder.Services.AddSingleton(quotaOptions);
builder.Services.AddScoped<QuotaService>();

builder.Services.AddScoped<DemoDataSeeder>();

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

// Reminders are NOT hosted here. They run as Azure Functions timer triggers
// in MiFuturoTrabajo.Functions, because App Service Free has no Always On:
// the site unloads when idle and a polling BackgroundService silently stops
// firing. A Consumption-plan timer has its own schedule and its own host.

// CORS. The allowed origins are configuration, not literals: the deployed
// client lives on a Static Web Apps hostname that is not knowable until the
// infrastructure exists, and localhost must not be baked into a production
// allowlist.
//
// BlazorClient:Url is the deployed origin. BlazorClient:AdditionalOrigins is a
// comma-separated list for anything else (a custom domain, a preview slot).
var blazorClientUrl = builder.Configuration["BlazorClient:Url"];

var allowedOrigins = new List<string>();

if (!string.IsNullOrWhiteSpace(blazorClientUrl))
{
    allowedOrigins.Add(blazorClientUrl.TrimEnd('/'));
}

allowedOrigins.AddRange(
    (builder.Configuration["BlazorClient:AdditionalOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(o => o.TrimEnd('/')));

if (!builder.Environment.IsProduction())
{
    // Only outside Production, and only these two.
    allowedOrigins.Add("http://localhost:5001");
    allowedOrigins.Add("https://localhost:5011");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(allowedOrigins.Distinct().ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    // Any origin, for Postman and Swagger. Registering it at all in Production
    // is a loaded gun lying next to a policy selector, so it does not exist
    // there - a misconfigured environment name cannot reach for it.
    if (!builder.Environment.IsProduction())
    {
        options.AddPolicy("AllowDevelopment", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    }
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

// Enable CORS - permissive outside Production, allowlist in Production.
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

// After authentication, so the per-user policy can actually see the user.
app.UseRateLimiter();

app.MapControllers();

// A liveness probe that touches nothing. App Service Free has no Always On,
// so this is also what a keep-warm ping would hit.
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .AllowAnonymous();

// Create the database and containers if they are not there yet.
//
// This used to run in Development only, which would have made the first
// deployment fail: nothing else creates the Cosmos containers, so every query
// against a fresh account would have thrown. It is idempotent and costs one
// metadata call at startup.
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        await dbContext.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("Database and containers ensured to exist.");
    }
    catch (Exception ex)
    {
        // Do not take the site down over this: a transient failure here would
        // otherwise turn into a crash loop, and the next request will surface
        // the real problem with a real error.
        app.Logger.LogError(ex, "Could not ensure the database exists.");
    }
}

// Optional demo account, created once at startup when configured. Seeding is
// idempotent and does nothing at all unless Demo:Enabled is true.
if (app.Configuration.GetValue<bool>("Demo:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();

    try
    {
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Demo seeding failed; the site will start without it.");
    }
}

app.Run();

// Make the Program class public for integration tests
public partial class Program { }
