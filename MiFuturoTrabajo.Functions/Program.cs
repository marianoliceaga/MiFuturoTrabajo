using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Email;
using MiFuturoTrabajo.Infrastructure.Reminders;

// Isolated worker, timer triggers only - no HTTP, so no ASP.NET Core
// integration and none of the middleware pipeline that comes with it.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services
            .AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights();

        // No InMemory option here, deliberately. The API offers one so it can
        // run locally without a store; a reminder host pointed at a throwaway
        // in-process database would read an empty set and mail nobody, which
        // looks identical to working. Cosmos or nothing.
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration["CosmosDb:ConnectionString"];
            var databaseName = configuration["CosmosDb:DatabaseName"];

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException(
                    "CosmosDb:ConnectionString and CosmosDb:DatabaseName must be configured for " +
                    "the reminder functions to read anything.");
            }

            options.UseCosmos(connectionString, databaseName);
        });

        var emailConnectionString = configuration["AzureCommunicationServices:ConnectionString"];
        var emailSenderAddress = configuration["AzureCommunicationServices:SenderAddress"];

        var emailIsConfigured =
            !string.IsNullOrWhiteSpace(emailConnectionString)
            && !emailConnectionString.Contains("YOUR_ACS")
            && !string.IsNullOrWhiteSpace(emailSenderAddress)
            && !emailSenderAddress.Contains("your-domain");

        services.AddSingleton<IEmailService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<EmailService>>();

            if (emailIsConfigured)
            {
                return new EmailService(emailConnectionString!, emailSenderAddress!, logger);
            }

            // This app exists only to send email, so running it unconfigured is
            // a no-op on a schedule. Logging the message keeps local runs
            // useful; Phase 7 sets the real values in app settings.
            logger.LogWarning(
                "No email provider configured. Reminder mail will be written to this log instead of sent.");

            return new NoOpEmailService(logger);
        });

        services.AddScoped<ReminderService>();
    })
    .Build();

host.Run();
