using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Letmework4U.Infrastructure;
using Letmework4U.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // Configure Custom JWT Authentication
        var jwtSecretKey = context.Configuration["Jwt:SecretKey"];
        var jwtIssuer = context.Configuration["Jwt:Issuer"] ?? "Letmework4U";
        var jwtAudience = context.Configuration["Jwt:Audience"] ?? "Letmework4U.Client";

        if (string.IsNullOrEmpty(jwtSecretKey))
        {
            throw new InvalidOperationException("JWT SecretKey is not configured in local.settings.json");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

        // Configure Entity Framework Core with Cosmos DB
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var cosmosConnectionString = context.Configuration["CosmosDb:ConnectionString"];
            var cosmosDatabaseName = context.Configuration["CosmosDb:DatabaseName"];

            if (!string.IsNullOrEmpty(cosmosConnectionString) && !string.IsNullOrEmpty(cosmosDatabaseName))
            {
                options.UseCosmos(cosmosConnectionString, cosmosDatabaseName);
            }
        });

        // Register Authentication Services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        // Register Email Service
        services.AddSingleton<IEmailService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetRequiredService<ILogger<EmailService>>();
            var connectionString = config["AzureCommunicationServices:ConnectionString"] ?? "dummy";
            var senderAddress = config["AzureCommunicationServices:SenderAddress"] ?? "test@example.com";

            return new EmailService(connectionString, senderAddress, logger);
        });
    })
    .Build();

host.Run();
